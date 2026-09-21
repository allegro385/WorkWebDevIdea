using System.ComponentModel.DataAnnotations;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Areas.Admin.Models;

/// <summary>お知らせ編集欄の入力です。ツールお知らせとサイトお知らせで同じ項目を使用します。</summary>
public sealed class NoticeEditInput
{
    /// <summary>更新対象のお知らせです。新規登録ではnullです。</summary>
    public int? NoticeId { get; set; }

    [Display(Name = "タイトル")]
    [Required(ErrorMessage = "タイトルを入力してください。")]
    [StringLength(200, ErrorMessage = "タイトルは200文字以内で入力してください。")]
    public string? Title { get; set; }

    [Display(Name = "内容")]
    [Required(ErrorMessage = "内容を入力してください。")]
    [StringLength(NoticeService.MaxContentLength, ErrorMessage = "内容は10,000文字以内で入力してください。")]
    public string? Content { get; set; }

    [Display(Name = "公開状態")]
    public bool IsPublished { get; set; }

    /// <summary>取得時のUpdateCountです。新規登録では0です。</summary>
    public int UpdateCount { get; set; }
}

/// <summary>お知らせ区画の表示情報です。サイト管理とツール編集で共有します。</summary>
public sealed class NoticeSectionViewModel
{
    /// <summary>SYSTEMまたはTOOLです。</summary>
    public string NoticeType { get; init; } = "SYSTEM";

    /// <summary>TOOLの場合の対象ツールです。</summary>
    public string? ToolId { get; init; }

    /// <summary>登録済みのお知らせを更新日の新しい順で並べた一覧です。</summary>
    public IReadOnlyList<NoticeListItem> Notices { get; init; } = [];

    /// <summary>編集欄の入力です。初期状態ではnullとし、編集欄を表示しません。</summary>
    public NoticeEditInput? Editor { get; set; }

    /// <summary>操作後にだけ表示する処理結果です。</summary>
    public OperationMessage? Message { get; set; }
}

/// <summary>お知らせ送信の確認画面の表示情報です。</summary>
/// <param name="NoticeType">SYSTEMまたはTOOLです。</param>
/// <param name="ToolId">TOOLの場合の対象ツールです。</param>
/// <param name="ToolName">TOOLの場合の対象ツール名です。</param>
/// <param name="ConfirmationId">実行時に一度だけ使用する確認IDです。</param>
/// <param name="NoticeCount">選択したお知らせの件数です。</param>
/// <param name="RecipientCount">確認時点の送信対象人数です。</param>
public sealed record NoticeSendConfirmViewModel(string NoticeType, string? ToolId, string? ToolName,
    Guid ConfirmationId, int NoticeCount, int RecipientCount);
