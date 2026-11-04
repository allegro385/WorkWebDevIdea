using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Logging;
using SalesSupport.Common.MasterData;
using SalesSupport.Portal.Web.Areas.Admin.Models;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>履歴の新旧をリリース日で判定し、バージョンの表記を制限しないことを確認します。</summary>
public sealed class ToolVersionRulesTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    /// <summary>利用者向けの履歴は番号の大小ではなく日付の新しい順に並べ、現在のバージョンより新しい行を出しません。</summary>
    [Fact]
    public async Task DetailListsHistoriesFromCurrentByReleaseDate()
    {
        await using var db = await SeedAsync(
            History(1, "9.9.9", "2026-01-10"),
            History(2, "1.0.0", "2026-03-01"),
            History(3, "V20260301", "2026-03-01", isCurrent: true),
            History(4, "第2版", "2026-08-10"));
        var service = new ToolQueryService(db, new ApplicationClock(TimeProvider.System));

        var detail = await service.GetDetailAsync("T001", UserId);

        Assert.Equal("V20260301", detail!.Version);
        Assert.Equal(["V20260301", "1.0.0", "9.9.9"], detail.Versions.Select(x => x.Version));
    }

    /// <summary>現在のバージョンが未選択なら、画面の補完値を使い履歴は表示しません。</summary>
    [Fact]
    public async Task DetailUsesDefaultVersionWithoutCurrent()
    {
        await using var db = await SeedAsync(History(1, "2026.11", "2026-03-01"));
        var service = new ToolQueryService(db, new ApplicationClock(TimeProvider.System));

        var detail = await service.GetDetailAsync("T001", UserId);

        Assert.Equal(ToolQueryService.DefaultVersion, detail!.Version);
        Assert.Empty(detail.Versions);
    }

    /// <summary>管理画面も同じ順序で全件を並べ、最新かつ現在のバージョンでない履歴だけを削除候補にします。</summary>
    [Fact]
    public async Task AdminListsAllHistoriesAndMarksNewestDeletable()
    {
        await using var db = await SeedAsync(
            History(1, "9.9.9", "2026-01-10", isCurrent: true),
            History(2, "1.0.0", "2026-03-01"),
            History(3, "第2版", "2026-03-01"));
        var service = Admin(db);

        var context = await service.GetEditContextAsync("T001", null);

        Assert.Equal(["第2版", "1.0.0", "9.9.9"], context!.Versions.Versions.Select(x => x.Version));
        Assert.Equal([true, false, false], context.Versions.Versions.Select(x => x.CanDelete));
    }

    /// <summary>番号が大きくても最新でない履歴は削除せず、最新の履歴は削除できます。</summary>
    [Fact]
    public async Task DeleteAllowsOnlyNewestHistory()
    {
        await using var db = await SeedAsync(
            History(1, "1.0.0", "2026-01-10", isCurrent: true),
            History(2, "9.9.9", "2026-02-01"),
            History(3, "2.0.0", "2026-03-01"));
        var service = Admin(db);

        Assert.Equal(ToolAdminOutcome.InvalidInput, (await service.DeleteVersionAsync("T001", 2, 0)).Outcome);
        Assert.Equal(ToolAdminOutcome.Saved, (await service.DeleteVersionAsync("T001", 3, 0)).Outcome);
    }

    /// <summary>一般的な文字は表記を問わず登録でき、前後の空白は保存しません。</summary>
    /// <param name="input">入力されたバージョンです。</param>
    /// <param name="saved">保存されることを期待する値です。</param>
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("01.100.0", "01.100.0")]
    [InlineData("2026.11", "2026.11")]
    [InlineData("1.0.0-beta", "1.0.0-beta")]
    [InlineData("V20260810", "V20260810")]
    [InlineData("第2版", "第2版")]
    [InlineData("Build #12 (rc)/a_b", "Build #12 (rc)/a_b")]
    [InlineData("  2.0  ", "2.0")]
    public async Task CreateAcceptsAnyOrdinaryText(string input, string saved)
    {
        await using var db = await SeedAsync();

        var result = await Admin(db).CreateVersionAsync("T001", Input(input));

        Assert.Equal(ToolAdminOutcome.Saved, result.Outcome);
        Assert.Equal(saved, (await db.ToolVersionHistories.AsNoTracking().SingleAsync()).Version);
    }

    /// <summary>未入力・空白だけ・制御文字を含む値は保存しません。</summary>
    /// <param name="input">入力されたバージョンです。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("1.0\n2")]
    [InlineData("1.0\t2")]
    [InlineData("1\u00010")]
    public async Task CreateRejectsBlankAndControlCharacters(string? input)
    {
        await using var db = await SeedAsync();

        var result = await Admin(db).CreateVersionAsync("T001", Input(input));

        Assert.Equal(ToolAdminOutcome.InvalidInput, result.Outcome);
        Assert.Empty(db.ToolVersionHistories);
    }

    /// <summary>バージョンの上限はDBの列と同じ50文字です。</summary>
    [Fact]
    public async Task CreateLimitsLengthToColumnSize()
    {
        await using var db = await SeedAsync();
        var service = Admin(db);

        Assert.Equal(ToolAdminOutcome.InvalidInput, (await service.CreateVersionAsync("T001", Input(new string('a', 51)))).Outcome);
        Assert.Equal(ToolAdminOutcome.Saved, (await service.CreateVersionAsync("T001", Input(new string('a', 50)))).Outcome);
    }

    /// <summary>前後の空白を除いた値で同一バージョンの重複を検出します。</summary>
    [Fact]
    public async Task CreateDetectsDuplicateAfterTrimming()
    {
        await using var db = await SeedAsync(History(1, "V20260810", "2026-08-10"));

        var result = await Admin(db).CreateVersionAsync("T001", Input(" V20260810 "));

        Assert.Equal(ToolAdminOutcome.InvalidInput, result.Outcome);
    }

    /// <summary>検証用のツール・利用者・履歴を保存した使い捨てのEFデータベースを作成します。</summary>
    /// <param name="histories">登録する履歴です。</param>
    /// <returns>データ投入済みのコンテキストです。</returns>
    private static async Task<PortalDbContext> SeedAsync(params ToolVersionHistory[] histories)
    {
        var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(AccountServiceTests.InMemoryLockQuery.Instance).Options);
        db.Users.Add(new ApplicationUser
        {
            Id = UserId, UserName = "admin", NormalizedUserName = "ADMIN", Email = "admin@example.invalid", NormalizedEmail = "ADMIN@EXAMPLE.INVALID",
            SecurityStamp = "stamp", DisplayName = "管理者", RoleCode = "ADMIN", IsActive = true
        });
        db.Tools.Add(new Tool { ToolId = "T001", ToolName = "検証ツール", OwnerUserId = UserId, ToolType = "WEB", Status = "PUBLIC", SortOrder = 1 });
        db.ToolVersionHistories.AddRange(histories);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    /// <summary>検証用の履歴1件を作成します。</summary>
    /// <param name="id">登録順を表す履歴IDです。</param>
    /// <param name="version">バージョンです。</param>
    /// <param name="releasedAt">リリース日です。</param>
    /// <param name="isCurrent">現在のバージョンとして選択する場合はtrueです。</param>
    /// <returns>検証用の履歴です。</returns>
    private static ToolVersionHistory History(int id, string version, string releasedAt, bool isCurrent = false) => new()
    {
        ToolHistoryId = id, ToolId = "T001", Version = version, ModifiedByUserId = UserId, ChangeDescription = "更新",
        ReleasedAt = DateOnly.Parse(releasedAt), IsCurrent = isCurrent
    };

    /// <summary>新規登録の入力を作成します。</summary>
    /// <param name="version">入力するバージョンです。</param>
    /// <returns>バージョン以外が有効な入力です。</returns>
    private static ToolVersionInput Input(string? version) => new()
    {
        Version = version, ReleasedAt = new DateOnly(2026, 11, 4), ModifiedByUserId = UserId, ChangeDescription = "更新"
    };

    /// <summary>コードマスタと操作ログに依存しない管理サービスを作成します。</summary>
    /// <param name="db">対象のコンテキストです。</param>
    /// <returns>検証対象のサービスです。</returns>
    private static ToolAdminService Admin(PortalDbContext db) => new(db, new EmptyCodes(), new NullActivityLogger());

    /// <summary>コードの選択肢を持たないコードマスタです。</summary>
    private sealed class EmptyCodes : ICodeMasterReader
    {
        /// <summary>空の選択肢を返します。</summary>
        public Task<IReadOnlyList<CodeOption>> GetOptionsAsync(string codeType, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CodeOption>>([]);
        /// <summary>該当なしを返します。</summary>
        public Task<CodeOption?> FindAsync(string codeType, string codeValue, CancellationToken ct = default) => Task.FromResult<CodeOption?>(null);
    }

    /// <summary>操作ログの記録を省略します。</summary>
    private sealed class NullActivityLogger : IActivityLogger
    {
        /// <summary>記録せず、省略した結果を返します。</summary>
        public Task<LogWriteResult> WriteAsync(ActivityEvent entry, CancellationToken ct = default) => Task.FromResult(LogWriteResult.Skipped);
    }
}
