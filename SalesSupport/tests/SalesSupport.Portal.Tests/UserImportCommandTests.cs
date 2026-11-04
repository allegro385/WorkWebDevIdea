using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Logging;
using SalesSupport.Portal.Web.Bootstrap;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>管理コマンドimport-usersの試行・確認・登録・更新と、秘密値を出さないことを確認します。</summary>
/// <remarks>InMemoryはトランザクションのロールバックを再現しないため、全件の取消しは使い捨てのSQL Serverで確認します。</remarks>
public sealed class UserImportCommandTests : IAsyncDisposable
{
    private const string Header = "LoginId\tEmail\tPassword\tDisplayName\tRoleCode\tIsActive";
    private static readonly string Password = Guid.NewGuid().ToString("N") + "Aa!";

    private readonly string folder = Directory.CreateTempSubdirectory("ss-import-").FullName;
    private readonly ServiceProvider provider;
    private readonly AsyncServiceScope scope;
    private readonly RecordingActivityLogger activity = new();
    private readonly StringWriter output = new();

    /// <summary>実際のUserManagerと使い捨てのEFデータベースを用意します。</summary>
    public UserImportCommandTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var database = Guid.NewGuid().ToString();
        services.AddDbContext<PortalDbContext>(options => options.UseInMemoryDatabase(database)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 6;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
        }).AddEntityFrameworkStores<PortalDbContext>();
        provider = services.BuildServiceProvider();
        scope = provider.CreateAsyncScope();
        Db.Roles.AddRange(new Role { RoleCode = "A", RoleName = "ロールA" }, new Role { RoleCode = "ADMIN", RoleName = "管理者" });
        Db.SaveChanges();
    }

    private PortalDbContext Db => scope.ServiceProvider.GetRequiredService<PortalDbContext>();
    private UserManager<ApplicationUser> Users => scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    /// <summary>コマンド名の判定はWebサーバー起動との分岐に使います。</summary>
    [Fact]
    public void RecognizesCommandName()
    {
        Assert.True(UserImportCommand.IsRequested(["import-users", "a.tsv"]));
        Assert.False(UserImportCommand.IsRequested(["add-test-user"]));
        Assert.False(UserImportCommand.IsRequested([]));
    }

    /// <summary>引数の不足・未定義オプション・読めないファイルを、DBへ触れずに入力不備として拒否します。</summary>
    [Fact]
    public async Task InvalidArgumentsAreRejected()
    {
        string[][] invalid = [["import-users"], ["import-users", "a.tsv", "--force"], ["import-users", "a.tsv", "b.tsv"], ["import-users", Path.Combine(folder, "none.tsv")]];

        foreach (var args in invalid) Assert.Equal(3, await RunAsync(args));

        Assert.Empty(Db.Users);
    }

    /// <summary>実行前に接続先のDB名と環境区分を表示し、接続文字列の秘密値は表示しません。</summary>
    [Fact]
    public async Task ShowsTargetWithoutSecrets()
    {
        await RunAsync(["import-users", WriteTsv(Row("staff1", "A")), "--dry-run"]);

        var text = output.ToString();
        Assert.Contains("PortalTest", text);
        Assert.Contains("DEVELOPMENT", text);
        Assert.DoesNotContain("connection-secret", text);
    }

    /// <summary>試行モードは件数だけを表示し、DBを変更せず操作ログも残しません。</summary>
    [Fact]
    public async Task DryRunReportsCountsWithoutSaving()
    {
        await CreateExistingAsync();
        var file = WriteTsv(Row("staff1", "A"), Row("staff2", "ADMIN"), Row("existing", "A"));

        Assert.Equal(0, await RunAsync(["import-users", file, "--dry-run"]));

        Assert.Contains("追加2件、更新0件、スキップ1件、エラー0件", output.ToString());
        Assert.Equal(1, await Db.Users.CountAsync());
        Assert.Empty(activity.Events);
    }

    /// <summary>Identityでハッシュ化して登録し、確認済みメール・ロール・有効状態・通知OFFを設定します。</summary>
    [Fact]
    public async Task CreatesUsersThroughIdentity()
    {
        var file = WriteTsv(Row("staff1", "ADMIN"), $"staff2\tstaff2@example.invalid\t{Password}\t利用者\tA\t0");

        Assert.Equal(0, await RunAsync(["import-users", file]));

        var user = (await Users.FindByNameAsync("staff1"))!;
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.True(await Users.CheckPasswordAsync(user, Password));
        Assert.True(user.EmailConfirmed);
        Assert.True(user.LockoutEnabled);
        Assert.True(user.IsActive);
        Assert.Equal("ADMIN", user.RoleCode);
        Assert.False((await Users.FindByNameAsync("staff2"))!.IsActive);
        var preference = await Db.UserPreferences.SingleAsync(x => x.UserId == user.Id);
        Assert.False(preference.SystemNoticeMailEnabled);
        Assert.False(preference.FavoriteToolNoticeMailEnabled);
    }

    /// <summary>既存ユーザーは既定でスキップし、値を変更しません。</summary>
    [Fact]
    public async Task SkipsExistingUserByDefault()
    {
        var existing = await CreateExistingAsync();
        var hash = existing.PasswordHash;

        Assert.Equal(0, await RunAsync(["import-users", WriteTsv($"existing\tnew@example.invalid\tother-password\t新しい名前\tADMIN\t0")]));

        var saved = await Db.Users.AsNoTracking().SingleAsync();
        Assert.Equal("existing@example.invalid", saved.Email);
        Assert.Equal("A", saved.RoleCode);
        Assert.Equal(hash, saved.PasswordHash);
        Assert.Contains("追加0件、更新0件、スキップ1件、エラー0件", output.ToString());
    }

    /// <summary>更新オプションでは項目を更新し、ロール変更でSecurityStampを更新します。パスワード欄が空なら変更しません。</summary>
    [Fact]
    public async Task UpdatesExistingUserAndKeepsPasswordWhenBlank()
    {
        var existing = await CreateExistingAsync();
        var hash = existing.PasswordHash;
        var stamp = existing.SecurityStamp;

        Assert.Equal(0, await RunAsync(["import-users", WriteTsv("existing\tnew@example.invalid\t\t新しい名前\tADMIN\t1"), "--update"]));

        var saved = await Db.Users.AsNoTracking().SingleAsync();
        Assert.Equal("new@example.invalid", saved.Email);
        Assert.Equal("新しい名前", saved.DisplayName);
        Assert.Equal("ADMIN", saved.RoleCode);
        Assert.True(saved.EmailConfirmed);
        Assert.Equal(hash, saved.PasswordHash);
        Assert.NotEqual(stamp, saved.SecurityStamp);
    }

    /// <summary>更新オプションでパスワードを指定すると変更し、SecurityStampも更新します。</summary>
    [Fact]
    public async Task UpdatesPasswordWhenProvided()
    {
        var existing = await CreateExistingAsync();
        var stamp = existing.SecurityStamp;

        await RunAsync(["import-users", WriteTsv("existing\texisting@example.invalid\tchanged-password\t既存\tA\t1"), "--update"]);

        var saved = (await Users.FindByNameAsync("existing"))!;
        Assert.True(await Users.CheckPasswordAsync(saved, "changed-password"));
        Assert.NotEqual(stamp, saved.SecurityStamp);
    }

    /// <summary>表示名だけの変更ではセッションを無効にしません。</summary>
    [Fact]
    public async Task DisplayNameChangeKeepsSecurityStamp()
    {
        var stamp = (await CreateExistingAsync()).SecurityStamp;

        await RunAsync(["import-users", WriteTsv("existing\texisting@example.invalid\t\t表示名のみ変更\tA\t1"), "--update"]);

        var saved = await Db.Users.AsNoTracking().SingleAsync();
        Assert.Equal("表示名のみ変更", saved.DisplayName);
        Assert.Equal(stamp, saved.SecurityStamp);
    }

    /// <summary>更新オプションでも差分のない行はスキップとして数え、本番でも確認と操作ログなしで終了します。</summary>
    [Fact]
    public async Task UnchangedRowIsCountedAsSkipped()
    {
        await CreateExistingAsync();

        Assert.Equal(0, await RunAsync(["import-users", WriteTsv("existing\texisting@example.invalid\t\t既存\tA\t1"), "--update"], "PRODUCTION"));

        Assert.Contains("追加0件、更新0件、スキップ1件、エラー0件", output.ToString());
        Assert.Contains("登録する内容はありません", output.ToString());
        Assert.Empty(activity.Events);
    }

    /// <summary>DBとの照合で不備があれば全行分を報告し、1件も登録しません。</summary>
    [Fact]
    public async Task DatabaseErrorsAreReportedAndNothingIsSaved()
    {
        await CreateExistingAsync();
        var file = WriteTsv(Row("staff1", "A"), Row("staff2", "UNKNOWN"), $"staff3\texisting@example.invalid\t{Password}\t名前\tA\t1",
            "staff4\tstaff4@example.invalid\t\t名前\tA\t1", "staff5\tstaff5@example.invalid\tabc\t名前\tA\t1");

        Assert.Equal(3, await RunAsync(["import-users", file]));

        var text = output.ToString();
        Assert.Contains("3行目", text);
        Assert.Contains("4行目", text);
        Assert.Contains("5行目", text);
        Assert.Contains("6行目", text);
        Assert.Contains("エラー4件", text);
        Assert.Equal(1, await Db.Users.CountAsync());
    }

    /// <summary>本番環境ではyの入力がなければ登録しません。</summary>
    /// <param name="answer">確認への入力です。</param>
    [Theory]
    [InlineData("n")]
    [InlineData("")]
    [InlineData("Y")]
    public async Task ProductionRequiresConfirmation(string answer)
    {
        Assert.Equal(2, await RunAsync(["import-users", WriteTsv(Row("staff1", "A"))], "PRODUCTION", answer));

        Assert.Empty(Db.Users);
        Assert.Empty(activity.Events);
    }

    /// <summary>本番環境でyを入力すると登録します。試行モードでは確認を求めません。</summary>
    [Fact]
    public async Task ProductionProceedsAfterConfirmation()
    {
        var file = WriteTsv(Row("staff1", "A"));

        Assert.Equal(0, await RunAsync(["import-users", file, "--dry-run"], "PRODUCTION", null));
        Assert.Empty(Db.Users);

        Assert.Equal(0, await RunAsync(["import-users", file], "PRODUCTION", "y"));
        Assert.Equal(1, await Db.Users.CountAsync());
    }

    /// <summary>登録結果は実行者・ファイル名・件数を操作ログへ記録し、パスワードを画面にもログにも出しません。</summary>
    [Fact]
    public async Task RecordsAuditWithoutSecrets()
    {
        await RunAsync(["import-users", WriteTsv(Row("staff1", "A"))]);

        var entry = Assert.Single(activity.Events);
        Assert.Equal("USER_IMPORT", entry.EventType);
        Assert.Equal("SUCCESS", entry.ResultCode);
        var changes = entry.Changes!.ToDictionary(x => x.Field, x => x.ToCode);
        Assert.Equal(Environment.UserName, changes["Operator"]);
        Assert.Equal("users.tsv", changes["FileName"]);
        Assert.Equal("1", changes["Added"]);
        Assert.Equal("0", changes["Updated"]);
        Assert.Equal("0", changes["Skipped"]);
        Assert.DoesNotContain(Password, output.ToString());
        Assert.DoesNotContain(entry.Changes!, x => x.ToCode == Password);
    }

    /// <summary>UTF-8以外のファイルを、例外内容を表示せず入力不備として拒否します。</summary>
    [Fact]
    public async Task InvalidUtf8IsRejected()
    {
        var file = Path.Combine(folder, "broken.tsv");
        await File.WriteAllBytesAsync(file, [0xff, 0xff, 0x41]);

        Assert.Equal(3, await RunAsync(["import-users", file]));
    }

    /// <summary>BOM付きのUTF-8でも見出しを認識します。</summary>
    [Fact]
    public async Task AcceptsUtf8WithBom()
    {
        var file = Path.Combine(folder, "bom.tsv");
        await File.WriteAllTextAsync(file, Header + "\n" + Row("staff1", "A"), new UTF8Encoding(true));

        Assert.Equal(0, await RunAsync(["import-users", file]));
    }

    /// <summary>試験資源を破棄します。</summary>
    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
        Directory.Delete(folder, recursive: true);
    }

    /// <summary>指定した環境区分でコマンドを実行します。</summary>
    /// <param name="args">コマンド引数です。</param>
    /// <param name="environmentCode">共通設定の環境区分です。</param>
    /// <param name="answer">確認への入力です。NULLは確認を求めないことを期待します。</param>
    /// <returns>終了コードです。</returns>
    private Task<int> RunAsync(string[] args, string environmentCode = "DEVELOPMENT", string? answer = null)
    {
        var connections = new ConnectionStringProvider(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SalesSupport"] = "Server=test-server;Database=PortalTest;User Id=test;Password=connection-secret"
        }).Build());
        var command = new UserImportCommand(new UserImportTsvReader(new UpperInvariantLookupNormalizer()), new UserImportService(Db, Users), activity,
            connections, Options.Create(new CommonOptions { EnvironmentCode = environmentCode }));
        return command.RunAsync(args, answer is null ? new ThrowingReader() : new StringReader(answer + "\n"), output);
    }

    /// <summary>見出し付きのTSVファイルを作成します。</summary>
    /// <param name="rows">本文の行です。</param>
    /// <returns>作成したファイルのパスです。</returns>
    private string WriteTsv(params string[] rows)
    {
        var path = Path.Combine(folder, "users.tsv");
        File.WriteAllText(path, Header + "\n" + string.Join('\n', rows), new UTF8Encoding(false));
        return path;
    }

    /// <summary>有効な1行を作成します。</summary>
    /// <param name="loginId">ログインIDです。メールにも使います。</param>
    /// <param name="roleCode">ロールです。</param>
    /// <returns>TSVの1行です。</returns>
    private static string Row(string loginId, string roleCode) => $"{loginId}\t{loginId}@example.invalid\t{Password}\t利用者\t{roleCode}\t1";

    /// <summary>既存ユーザーを1件作成します。</summary>
    /// <returns>作成したユーザーです。</returns>
    private async Task<ApplicationUser> CreateExistingAsync()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = "existing", Email = "existing@example.invalid", EmailConfirmed = true, DisplayName = "既存",
            RoleCode = "A", IsActive = true, LockoutEnabled = true
        };
        Assert.True((await Users.CreateAsync(user, "initial-password")).Succeeded);
        return user;
    }

    /// <summary>確認を求めないはずの処理が入力を読んだことを検出します。</summary>
    private sealed class ThrowingReader : TextReader
    {
        /// <summary>入力を読んだ時点でテストを失敗させます。</summary>
        public override ValueTask<string?> ReadLineAsync(CancellationToken ct) => throw new InvalidOperationException();
    }

    /// <summary>操作ログの内容を記録します。</summary>
    private sealed class RecordingActivityLogger : IActivityLogger
    {
        public List<ActivityEvent> Events { get; } = [];

        /// <summary>記録だけを行います。</summary>
        public Task<LogWriteResult> WriteAsync(ActivityEvent entry, CancellationToken ct = default)
        {
            Events.Add(entry);
            return Task.FromResult(LogWriteResult.Written);
        }
    }
}
