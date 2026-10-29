using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Logging;
using SalesSupport.Portal.Web.Areas.Admin.Models;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>Webツールの起動先を同一サイト内のパスだけで扱い、絶対URL・スキーム相対URL・制御文字を起動先にしないことを確認します。</summary>
public sealed class ToolWebLaunchPathTests
{
    /// <summary>登録済みが同一サイト内のパスのときだけ起動先を返します。通信方式に依存しません。</summary>
    /// <param name="webAppUrl">ツールへ登録済みの起動先です。</param>
    /// <param name="expected">期待する戻り値です。NULLは起動させないことを期待します。</param>
    [Theory]
    [InlineData("/tools/T001/app", "/tools/T001/app")]
    [InlineData("/tools/T001/app?mode=1", "/tools/T001/app?mode=1")]
    [InlineData("https://sales-support/tools/T001/app", null)]
    [InlineData("http://sales-support/tools/T001/app", null)]
    [InlineData("//evil.example/tools/T001/app", null)]
    [InlineData("/tools\\T001/app", null)]
    [InlineData("tools/T001/app", null)]
    [InlineData(null, null)]
    public async Task WebLaunchUrlReturnsSitePathsOnly(string? webAppUrl, string? expected)
    {
        await using var db = CreateContext();
        db.Tools.Add(Tool("WEB", webAppUrl));
        await db.SaveChangesAsync();
        var service = new ToolQueryService(db, Clock);

        Assert.Equal(expected, await service.GetWebLaunchUrlAsync("T001"));
    }

    /// <summary>Webツール以外には起動先を持たせません。</summary>
    [Fact]
    public async Task WebLaunchUrlIsNotProvidedForOtherToolTypes()
    {
        await using var db = CreateContext();
        db.Tools.Add(Tool("DESKTOP", "/tools/T001/app"));
        await db.SaveChangesAsync();
        var service = new ToolQueryService(db, Clock);

        Assert.Null(await service.GetWebLaunchUrlAsync("T001"));
    }

    /// <summary>管理画面のページURLも同一サイト内のパスだけを受け付け、それ以外は項目エラーにします。</summary>
    /// <param name="input">入力されたページURLです。</param>
    /// <param name="saved">保存されることを期待する場合はtrueです。</param>
    [Theory]
    [InlineData("/tools/T001/app", true)]
    [InlineData("https://sales-support/tools/T001/app", false)]
    [InlineData("http://sales-support/tools/T001/app", false)]
    [InlineData("//evil.example/tools/T001/app", false)]
    [InlineData("/tools\\T001/app", false)]
    [InlineData("tools/T001/app", false)]
    public async Task SaveWebUrlAcceptsSitePathsOnly(string input, bool saved)
    {
        await using var db = CreateContext();
        db.Tools.Add(Tool("WEB", null));
        await db.SaveChangesAsync();
        var service = new ToolFileAdminService(db, new RejectStorage(), new RejectPolicies(), Clock,
            new NullActivityLogger(), new NullErrorLogger());

        var result = await service.SaveWebUrlAsync("T001", input, updateCount: 0);

        Assert.Equal(saved ? ToolAdminOutcome.Saved : ToolAdminOutcome.InvalidInput, result.Outcome);
        Assert.Equal(saved ? input : null, (await db.Tools.AsNoTracking().SingleAsync(x => x.ToolId == "T001")).WebAppUrl);
    }

    /// <summary>上限を超えるページURLは保存しません。</summary>
    [Fact]
    public async Task SaveWebUrlRejectsTooLongPath()
    {
        await using var db = CreateContext();
        db.Tools.Add(Tool("WEB", null));
        await db.SaveChangesAsync();
        var service = new ToolFileAdminService(db, new RejectStorage(), new RejectPolicies(), Clock,
            new NullActivityLogger(), new NullErrorLogger());

        var result = await service.SaveWebUrlAsync("T001", "/" + new string('a', 2048), updateCount: 0);

        Assert.Equal(ToolAdminOutcome.InvalidInput, result.Outcome);
        Assert.Null((await db.Tools.AsNoTracking().SingleAsync(x => x.ToolId == "T001")).WebAppUrl);
    }

    /// <summary>検証用のツール1件を作成します。</summary>
    /// <param name="toolType">提供種別です。</param>
    /// <param name="webAppUrl">起動先です。未登録はNULLです。</param>
    /// <returns>検証用のツールです。</returns>
    private static Tool Tool(string toolType, string? webAppUrl) => new()
    {
        ToolId = "T001", CategoryId = 1, ToolName = "検証ツール", OwnerUserId = Guid.NewGuid(),
        ToolType = toolType, WebAppUrl = webAppUrl, Status = "PUBLIC", SortOrder = 1
    };

    /// <summary>JSTへの変換だけに使う実時刻の時計です。</summary>
    private static IApplicationClock Clock => new ApplicationClock(TimeProvider.System);

    /// <summary>SQLへ接続しない使い捨てのEFデータベースを作成します。</summary>
    private static PortalDbContext CreateContext() => new(new DbContextOptionsBuilder<PortalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>起動先の検証ではファイル操作を行わないため、呼出しを失敗として検出します。</summary>
    private sealed class RejectStorage : IFileStorage
    {
        /// <summary>一時保存の呼出しを検出します。</summary>
        public Task<TemporaryFileHandle> SaveTemporaryAsync(TemporaryFileRequest request, Stream source, CancellationToken ct = default) =>
            throw new InvalidOperationException("ファイルを保存しました。");
        /// <summary>永続保存の呼出しを検出します。</summary>
        public Task<StoredFile> SavePermanentAsync(PermanentFileRequest request, Stream source, CancellationToken ct = default) =>
            throw new InvalidOperationException("ファイルを保存しました。");
        /// <summary>読取りの呼出しを検出します。</summary>
        public Task<Stream> OpenReadAsync(IStoredFileReference reference, CancellationToken ct = default) =>
            throw new InvalidOperationException("ファイルを読み取りました。");
        /// <summary>削除の呼出しを検出します。</summary>
        public Task<FileDeleteResult> DeleteAsync(IStoredFileReference reference, CancellationToken ct = default) =>
            throw new InvalidOperationException("ファイルを削除しました。");
    }

    /// <summary>起動先の検証ではアップロード条件を参照しないため、呼出しを失敗として検出します。</summary>
    private sealed class RejectPolicies : IUploadPolicyProvider
    {
        /// <summary>アップロード条件の取得を検出します。</summary>
        public Task<UploadPolicySnapshot> GetAsync(UploadPurpose purpose, string? toolId = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("アップロード条件を取得しました。");
    }

    /// <summary>操作ログの記録を省略します。保存結果の判定には影響しません。</summary>
    private sealed class NullActivityLogger : IActivityLogger
    {
        /// <summary>記録せず、省略した結果を返します。</summary>
        public Task<LogWriteResult> WriteAsync(ActivityEvent entry, CancellationToken ct = default) => Task.FromResult(LogWriteResult.Skipped);
    }

    /// <summary>障害ログの記録を省略します。</summary>
    private sealed class NullErrorLogger : ISystemErrorLogger
    {
        /// <summary>記録せず、省略した結果を返します。</summary>
        public Task<LogWriteResult> WriteAsync(SystemErrorEvent entry, CancellationToken ct = default) => Task.FromResult(LogWriteResult.Skipped);
    }
}
