namespace SalesSupport.Portal.Web.Models;

/// <summary>画面へ表示する公開済みお知らせ1件です。</summary>
/// <param name="NoticeId">お知らせの識別子です。</param>
/// <param name="Title">お知らせの表題です。</param>
/// <param name="Content">改行を保持したまま表示する本文です。HTMLとしては描画しません。</param>
/// <param name="Date">更新日をJSTの日付へ変換した値です。</param>
/// <param name="ShowDate">同じ日付が連続する2件目以降ではfalseにし、日付表示を省略します。</param>
public sealed record PortalNoticeView(int NoticeId, string Title, string Content, DateOnly Date, bool ShowDate);

/// <summary>P002 ポータルトップの表示情報です。</summary>
/// <param name="Notices">公開中のシステムお知らせです。</param>
/// <param name="IsAdmin">管理画面のメニューカードを表示するかどうかです。認可の代替にはしません。</param>
public sealed record HomeViewModel(IReadOnlyList<PortalNoticeView> Notices, bool IsAdmin);
