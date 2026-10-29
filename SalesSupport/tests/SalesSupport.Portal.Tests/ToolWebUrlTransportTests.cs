using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Logging;
using SalesSupport.Portal.Web.Areas.Admin.Models;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>Webツールの起動URLを、サイトの通信方式と同じスキーム（HTTP運用ではhttp）でだけ同一サイト配下として扱うことを確認します。</summary>
public sealed class ToolWebUrlTransportTests
{
    /// <summary>通信方式に一致するスキームの起動URLだけを返し、一致しないURLは起動させません。</summary>
    /// <param name="portalBaseUrl">共通設定のPortalのURLです。</param>
    /// <param name="transportMode">サイトの通信方式です。</param>
    /// <param name="webAppUrl">ツールへ登録済みの起動URLです。</param>
    /// <param name="expected">期待する戻り値です。NULLは起動させないことを期待します。</param>
    [Theory]
    [InlineData("http://sales-support/", SiteTransportMode.Http, "http://sales-support/tools/T001/app", "http://sales-support/tools/T001/app")]
    [InlineData("http://sales-support/", SiteTransportMode.Http, "https://sales-support/tools/T001/app", null)]
    [InlineData("https://sales-support/", SiteTransportMode.Https, "https://sales-support/tools/T001/app", "https://sales-support/tools/T001/app")]
    [InlineData("https://sales-support/", SiteTransportMode.Https, "http://sales-support/tools/T001/app", null)]
    [InlineData("http://sales-support/", SiteTransportMode.Http, "http://other-host/tools/T001/app", null)]
    public async Task WebLaunchUrlRequiresSiteScheme(string portalBaseUrl, SiteTransportMode transportMode, string webAppUrl, string? expected)
    {
        await using var db = CreateContext();
        db.Tools.Add(new Tool { ToolId = "T001", CategoryId = 1, ToolName = "検証ツール", OwnerUserId = Guid.NewGuid(),
            ToolType = "WEB", WebAppUrl = webAppUrl, Status = "PUBLIC", SortOrder = 1 });
        await db.SaveChangesAsync();
        var service = new ToolQueryService(db, Clock, Options(portalBaseUrl, transportMode));

        Assert.Equal(expected, await service.GetWebLaunchUrlAsync("T001"));
    }

    /// <summary>管理画面のページURLも通信方式と同じスキームだけを受け付け、一致しない入力は項目エラーにします。</summary>
    /// <param name="portalBaseUrl">共通設定のPortalのURLです。</param>
    /// <param name="transportMode">サイトの通信方式です。</param>
    /// <param name="input">入力されたページURLです。</param>
    /// <param name="saved">保存されることを期待する場合はtrueです。</param>
    [Theory]
    [InlineData("http://sales-support/", SiteTransportMode.Http, "http://sales-support/tools/T001/app", true)]
    [InlineData("http://sales-support/", SiteTransportMode.Http, "https://sales-support/tools/T001/app", false)]
    [InlineData("https://sales-support/", SiteTransportMode.Https, "https://sales-support/tools/T001/app", true)]
    [InlineData("https://sales-support/", SiteTransportMode.Https, "http://sales-support/tools/T001/app", false)]
    public async Task SaveWebUrlRequiresSiteScheme(string portalBaseUrl, SiteTransportMode transportMode, string input, bool saved)
    {
        await using var db = CreateContext();
        db.Tools.Add(new Tool { ToolId = "T001", CategoryId = 1, ToolName = "検証ツール", OwnerUserId = Guid.NewGuid(),
            ToolType = "WEB", Status = "HIDDEN", SortOrder = 1 });
        await db.SaveChangesAsync();
        var service = new ToolFileAdminService(db, new RejectStorage(), new RejectPolicies(), Clock,
            new NullActivityLogger(), new NullErrorLogger(), Options(portalBaseUrl, transportMode));

        var result = await service.SaveWebUrlAsync("T001", input, updateCount: 0);

        Assert.Equal(saved ? ToolAdminOutcome.Saved : ToolAdminOutcome.InvalidInput, result.Outcome);
        Assert.Equal(saved ? input : null, (await db.Tools.AsNoTracking().SingleAsync(x => x.ToolId == "T001")).WebAppUrl);
    }

    /// <summary>JSTへの変換だけに使う実時刻の時計です。</summary>
    private static IApplicationClock Clock => new ApplicationClock(TimeProvider.System);

    /// <summary>PortalのURLと通信方式だけを与えた共通設定を作成します。</summary>
    /// <param name="portalBaseUrl">PortalのURLです。</param>
    /// <param name="transportMode">サイトの通信方式です。</param>
    /// <returns>検証用の共通設定です。</returns>
    private static IOptions<CommonOptions> Options(string portalBaseUrl, SiteTransportMode transportMode) =>
        Microsoft.Extensions.Options.Options.Create(new CommonOptions
        {
            Kind = ApplicationKind.Portal, EnvironmentCode = "PRODUCTION", PortalBaseUrl = portalBaseUrl, TransportMode = transportMode
        });

    /// <summary>SQLへ接続しない使い捨てのEFデータベースを作成します。</summary>
    private static PortalDbContext CreateContext() => new(new DbContextOptionsBuilder<PortalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>URL検証ではファイル操作を行わないため、呼出しを失敗として検出します。</summary>
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

    /// <summary>URL検証ではアップロード条件を参照しないため、呼出しを失敗として検出します。</summary>
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
