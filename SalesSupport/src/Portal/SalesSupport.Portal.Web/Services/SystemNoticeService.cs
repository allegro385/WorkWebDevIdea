using SalesSupport.Common.DateTime;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Models;

namespace SalesSupport.Portal.Web.Services;

/// <summary>画面へ表示する公開中のシステムお知らせを取得します。</summary>
public interface ISystemNoticeService
{
    /// <summary>公開中のシステムお知らせを日付の新しい順で返します。</summary>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>トップとツール一覧で同じ内容・同じ並びのお知らせです。</returns>
    Task<IReadOnlyList<PortalNoticeView>> GetPublishedAsync(CancellationToken ct = default);
}

/// <summary>トップとツール一覧が同じ条件・同じ並びでお知らせを参照できるよう、取得処理を1か所にまとめます。</summary>
public sealed class SystemNoticeService(PortalDbContext db, IApplicationClock clock) : ISystemNoticeService
{
    /// <summary>非公開のお知らせとツールのお知らせは取得しません。</summary>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>公開中のシステムお知らせです。</returns>
    public Task<IReadOnlyList<PortalNoticeView>> GetPublishedAsync(CancellationToken ct = default) =>
        NoticeQuery.ReadPublishedAsync(db, clock, "SYSTEM", null, ct);
}
