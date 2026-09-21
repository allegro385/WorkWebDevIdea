using SalesSupport.Common.DateTime;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Models;

namespace SalesSupport.Portal.Web.Services;

/// <summary>ポータルトップの表示内容を取得します。</summary>
public interface IHomeService
{
    /// <summary>公開中のシステムお知らせを日付の新しい順で返します。</summary>
    Task<IReadOnlyList<PortalNoticeView>> GetSystemNoticesAsync(CancellationToken ct = default);
}

/// <summary>トップ画面が必要とする参照だけを行います。</summary>
public sealed class HomeService(PortalDbContext db, IApplicationClock clock) : IHomeService
{
    /// <summary>非公開のお知らせとツールのお知らせは取得しません。</summary>
    public Task<IReadOnlyList<PortalNoticeView>> GetSystemNoticesAsync(CancellationToken ct = default) =>
        NoticeQuery.ReadPublishedAsync(db, clock, "SYSTEM", null, ct);
}
