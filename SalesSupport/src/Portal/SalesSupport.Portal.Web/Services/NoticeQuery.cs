using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.DateTime;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Models;

namespace SalesSupport.Portal.Web.Services;

/// <summary>公開中のお知らせをトップとツール詳細で同じ条件・同じ並びで取得します。</summary>
internal static class NoticeQuery
{
    /// <summary>公開中のお知らせを更新日の新しい順で取得し、連続する同日の日付表示を省略します。</summary>
    /// <param name="db">Portalの保存単位です。</param>
    /// <param name="clock">更新日をJSTへ変換するための実時刻です。</param>
    /// <param name="noticeType">SYSTEMまたはTOOLです。</param>
    /// <param name="toolId">TOOLの場合の対象ツールです。SYSTEMではnullを渡します。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    public static async Task<IReadOnlyList<PortalNoticeView>> ReadPublishedAsync(PortalDbContext db, IApplicationClock clock,
        string noticeType, string? toolId, CancellationToken ct)
    {
        var rows = await db.Notices.AsNoTracking()
            .Where(x => x.NoticeType == noticeType && x.ToolId == toolId && x.IsPublished)
            .OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.NoticeId)
            .Select(x => new { x.NoticeId, x.Title, x.Content, x.UpdatedAt })
            .ToListAsync(ct);

        var notices = new List<PortalNoticeView>(rows.Count);
        DateOnly? previous = null;
        foreach (var row in rows)
        {
            var date = DateOnly.FromDateTime(clock.ToJst(new DateTimeOffset(System.DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc))).DateTime);
            notices.Add(new PortalNoticeView(row.NoticeId, row.Title, row.Content, date, date != previous));
            previous = date;
        }
        return notices;
    }
}
