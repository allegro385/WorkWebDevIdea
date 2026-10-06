using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Portal.Web.Bootstrap;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>開発専用TSVコマンドの環境制限・失敗時停止・Identity経由の保存を確認します。</summary>
public sealed class LocalTestUserImportCommandTests
{
    private static readonly string TestPassword = Guid.NewGuid().ToString("N") + "Aa!";

    /// <summary>どちらかが開発環境以外なら、標準入力とユーザー保存に触れません。</summary>
    [Theory]
    [InlineData("Production", "DEVELOPMENT")]
    [InlineData("Development", "PRODUCTION")]
    public async Task NonDevelopmentRejectsBeforeReading(string hostEnvironment, string portalEnvironment)
    {
        var provisioner = new StubProvisioner();
        var command = CreateCommand(provisioner, new StubConsole(), hostEnvironment, portalEnvironment);
        Assert.Equal(2, await command.RunAsync([LocalTestUserImportCommand.Name], new ThrowingReader(), true));
        Assert.Empty(provisioner.Inputs);
    }

    /// <summary>ロールの未定義値・余分な引数・標準入力なしをDB保存前に拒否します。</summary>
    [Fact]
    public async Task InvalidArgumentsAndInteractiveInputAreRejected()
    {
        var provisioner = new StubProvisioner();
        var command = CreateCommand(provisioner, new StubConsole());
        string[][] invalidArgs = [[], [LocalTestUserImportCommand.Name, "--role"],
            [LocalTestUserImportCommand.Name, "--role", "OTHER"], [LocalTestUserImportCommand.Name, "--environment", "Development"],
            [LocalTestUserImportCommand.Name, "file.tsv"]];
        foreach (var args in invalidArgs)
            Assert.Equal(3, await command.RunAsync(args, new ThrowingReader(), true));
        Assert.Equal(3, await command.RunAsync([LocalTestUserImportCommand.Name], new ThrowingReader(), false));
        Assert.Empty(provisioner.Inputs);
        Assert.True(LocalTestUserImportCommand.IsRequested([LocalTestUserImportCommand.Name, "--role"]));
        Assert.False(LocalTestUserImportCommand.IsRequested([]));
    }

    /// <summary>登録前に全行の形式を検証し、後半の不正があれば先行行も保存しません。</summary>
    [Fact]
    public async Task MalformedLaterRowDoesNotCreateEarlierUser()
    {
        var provisioner = new StubProvisioner();
        var console = new StubConsole();
        var command = CreateCommand(provisioner, console);
        Assert.Equal(3, await command.RunAsync([LocalTestUserImportCommand.Name], new StringReader(Row(1) + "\ninvalid"), true));
        Assert.Empty(provisioner.Inputs);
        Assert.Contains(console.Messages, message => message.StartsWith("2行目：", StringComparison.Ordinal));
        Assert.DoesNotContain(console.Messages, message => message.Contains(TestPassword, StringComparison.Ordinal));
    }

    /// <summary>省略時Aと、指定時ADMINを4列の入力へ補い、複数ユーザーを順に作成します。</summary>
    [Theory]
    [InlineData(false, "A")]
    [InlineData(true, "ADMIN")]
    public async Task RoleIsSpecifiedOutsideFourColumns(bool explicitRole, string expectedRole)
    {
        var provisioner = new StubProvisioner();
        var console = new StubConsole();
        var command = CreateCommand(provisioner, console);
        var args = explicitRole ? new[] { LocalTestUserImportCommand.Name, "--role", expectedRole } : [LocalTestUserImportCommand.Name];
        Assert.Equal(0, await command.RunAsync(args, new StringReader(Row(1) + "\n" + Row(2)), true));
        Assert.Equal(2, provisioner.Inputs.Count);
        Assert.All(provisioner.Inputs, user => Assert.Equal(expectedRole, user.RoleCode));
        Assert.All(provisioner.Inputs, user => Assert.Equal(TestPassword, user.Password));
        Assert.Contains(console.Messages, message => message.Contains("2件", StringComparison.Ordinal));
        Assert.DoesNotContain(console.Messages, message => message.Contains(TestPassword, StringComparison.Ordinal));
    }

    /// <summary>保存拒否・DB失敗・例外では後続の保存を止め、失敗行と確定済み件数だけを伝えます。</summary>
    [Theory]
    [InlineData(LocalTestUserOutcome.Rejected, false)]
    [InlineData(LocalTestUserOutcome.Failed, false)]
    [InlineData(LocalTestUserOutcome.Failed, true)]
    public async Task FirstSaveFailureStopsFollowingRows(LocalTestUserOutcome failure, bool throws)
    {
        var provisioner = new StubProvisioner { FailAt = 2, Failure = failure, Throws = throws };
        var console = new StubConsole();
        var command = CreateCommand(provisioner, console);
        Assert.Equal(1, await command.RunAsync([LocalTestUserImportCommand.Name], new StringReader(Row(1) + "\n" + Row(2) + "\n" + Row(3)), true));
        Assert.Equal(2, provisioner.Inputs.Count);
        Assert.Contains(console.Messages, message => message.StartsWith("2行目：", StringComparison.Ordinal));
        Assert.Contains(console.Messages, message => message.Contains("登録済み1件", StringComparison.Ordinal));
        Assert.DoesNotContain(console.Messages, message => message.Contains(TestPassword, StringComparison.Ordinal));
    }

    /// <summary>不正UTF-8の入力を秘密の例外内容を表示せずに拒否します。</summary>
    [Fact]
    public async Task InvalidUtf8IsRejectedWithoutSaving()
    {
        var provisioner = new StubProvisioner();
        using var bytes = new MemoryStream([0xff, 0xff]);
        using var input = new StreamReader(bytes, new UTF8Encoding(false, true), false);
        Assert.Equal(3, await CreateCommand(provisioner, new StubConsole()).RunAsync([LocalTestUserImportCommand.Name], input, true));
        Assert.Empty(provisioner.Inputs);
    }

    /// <summary>実際のUserManagerで別Guid・ハッシュ・通知OFFを保存し、重複取込が既存値を変えないことを確認します。</summary>
    [Fact]
    public async Task IdentityCreatesHashedUsersAndDuplicateImportDoesNotOverwrite()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PortalDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true).AddEntityFrameworkStores<PortalDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        // InMemoryは行トランザクションのロールバック・SQL制約を再現しません。Identity保存と既存値保持だけを確認します。
        db.Roles.Add(new Role { RoleCode = "A", RoleName = "ロールA" });
        await db.SaveChangesAsync();
        var console = new StubConsole();
        var command = CreateCommand(new LocalTestUserProvisioner(db, users), console);
        var input = Row(1) + "\n" + Row(2);
        Assert.Equal(0, await command.RunAsync([LocalTestUserImportCommand.Name], new StringReader(input), true));
        var saved = await db.Users.OrderBy(user => user.UserName).ToListAsync();
        Assert.Equal(2, saved.Count);
        Assert.NotEqual(saved[0].Id, saved[1].Id);
        foreach (var user in saved)
        {
            Assert.NotEqual(Guid.Empty, user.Id);
            Assert.NotEqual(TestPassword, user.PasswordHash);
            Assert.True(await users.CheckPasswordAsync(user, TestPassword));
            Assert.True(user.IsActive);
            Assert.Equal("A", user.RoleCode);
            var preference = await db.UserPreferences.SingleAsync(pref => pref.UserId == user.Id);
            Assert.False(preference.SystemNoticeMailEnabled);
            Assert.False(preference.FavoriteToolNoticeMailEnabled);
        }
        var originalHash = saved[0].PasswordHash;
        Assert.Equal(1, await command.RunAsync([LocalTestUserImportCommand.Name], new StringReader(input), true));
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(originalHash, (await db.Users.SingleAsync(user => user.Id == saved[0].Id)).PasswordHash);
        Assert.DoesNotContain(console.Messages, message => message.Contains(TestPassword, StringComparison.Ordinal));
    }

    /// <summary>入力・保存・表示の境界を差し替えて、指定環境のコマンドを作ります。</summary>
    private static LocalTestUserImportCommand CreateCommand(ILocalTestUserProvisioner provisioner, StubConsole console,
        string hostEnvironment = "Development", string portalEnvironment = "DEVELOPMENT") =>
        new(new StubEnvironment { EnvironmentName = hostEnvironment }, Options.Create(new CommonOptions { EnvironmentCode = portalEnvironment }),
            new LocalTestUserTsvReader(new UpperInvariantLookupNormalizer()), provisioner, console);

    /// <summary>登録値をテストごとに表示せず、指定順の4列入力を作ります。</summary>
    private static string Row(int index) => $"staff{index}\tstaff{index}@example.invalid\t{TestPassword}\t試験利用者{index}";

    /// <summary>指定した保存要求だけを拒否して、後続行の処理停止を観測します。</summary>
    private sealed class StubProvisioner : ILocalTestUserProvisioner
    {
        public List<LocalTestUserInput> Inputs { get; } = [];
        public int FailAt { get; init; }
        public LocalTestUserOutcome Failure { get; init; }
        public bool Throws { get; init; }

        /// <summary>要求を記録して成否または秘密を含む例外を返し、表示時の秘匿を検証します。</summary>
        public Task<LocalTestUserOutcome> CreateAsync(LocalTestUserInput input, CancellationToken ct = default)
        {
            Inputs.Add(input);
            if (Inputs.Count != FailAt) return Task.FromResult(LocalTestUserOutcome.Created);
            if (Throws) throw new InvalidOperationException(input.Password);
            return Task.FromResult(Failure);
        }
    }

    /// <summary>取込中に対話入力しないことと、表示内容に秘密が含まれないことを確認します。</summary>
    private sealed class StubConsole : ILocalTestUserConsole
    {
        public List<string> Messages { get; } = [];
        /// <summary>コマンドの表示結果を記録します。</summary>
        public void WriteLine(string message) => Messages.Add(message);
        /// <summary>TSV取込では対話入力を求めた時点でテストを失敗させます。</summary>
        public string? ReadLine() => throw new InvalidOperationException();
        /// <summary>TSV取込では対話パスワード入力を求めた時点でテストを失敗させます。</summary>
        public string ReadSecret() => throw new InvalidOperationException();
    }

    /// <summary>環境・引数の拒否前に入力を読んだことを検出します。</summary>
    private sealed class ThrowingReader : TextReader
    {
        /// <summary>前提条件で拒否された処理がTSVに触れたら例外を返します。</summary>
        public override ValueTask<string?> ReadLineAsync(CancellationToken ct) => throw new InvalidOperationException();
    }

    /// <summary>二重の開発環境制限をホスト側から検証します。</summary>
    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "SalesSupport.Portal.Web";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
