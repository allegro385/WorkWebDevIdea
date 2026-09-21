using System.ComponentModel.DataAnnotations;
using SalesSupport.Common.MasterData;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Areas.Admin.Models;

/// <summary>問い合わせ管理の検索条件です。指定した条件だけをANDで適用します。</summary>
public sealed class InquirySearchInput
{
    [Display(Name = "問い合わせID")]
    [StringLength(12)]
    public string? InquiryId { get; set; }

    [Display(Name = "送信期間（開始）")]
    public DateOnly? From { get; set; }

    [Display(Name = "送信期間（終了）")]
    public DateOnly? To { get; set; }

    [Display(Name = "カテゴリ")]
    public string? CategoryCode { get; set; }

    [Display(Name = "対象")]
    public string? Target { get; set; }

    [Display(Name = "担当者")]
    public Guid? AssigneeUserId { get; set; }

    [Display(Name = "ステータス")]
    public string? Status { get; set; }

    [Display(Name = "管理者用備考")]
    [StringLength(2000)]
    public string? AdminNote { get; set; }

    [Display(Name = "本文")]
    [StringLength(2000)]
    public string? Content { get; set; }
}

/// <summary>一覧・詳細から受け取る更新内容です。送信者と本文は変更できません。</summary>
public sealed class InquiryEditRow
{
    /// <summary>更新対象の問い合わせです。</summary>
    public string? InquiryId { get; set; }

    /// <summary>取得時のUpdateCountです。</summary>
    public int UpdateCount { get; set; }

    [Display(Name = "カテゴリ")]
    public string? CategoryCode { get; set; }

    [Display(Name = "対象")]
    public string? Target { get; set; }

    [Display(Name = "担当者")]
    public Guid? AssigneeUserId { get; set; }

    [Display(Name = "ステータス")]
    public string? Status { get; set; }

    [Display(Name = "管理者用備考")]
    [StringLength(2000, ErrorMessage = "管理者用備考は2,000文字以内で入力してください。")]
    public string? AdminNote { get; set; }
}

/// <summary>一覧の変更行を一括保存するための入力です。</summary>
public sealed class InquiryEditInput
{
    /// <summary>画面に表示していた全行です。値が変わった行だけを保存します。</summary>
    public List<InquiryEditRow> Rows { get; set; } = [];
}

/// <summary>A005 問い合わせ管理の一覧画面の表示情報です。</summary>
public sealed class InquiryListViewModel
{
    /// <summary>入力された検索条件です。</summary>
    public InquirySearchInput Search { get; init; } = new();

    /// <summary>条件に一致した問い合わせです。</summary>
    public IReadOnlyList<InquiryListItem> Inquiries { get; init; } = [];

    /// <summary>カテゴリの選択肢です。</summary>
    public IReadOnlyList<CodeOption> Categories { get; init; } = [];

    /// <summary>ステータスの選択肢です。</summary>
    public IReadOnlyList<CodeOption> Statuses { get; init; } = [];

    /// <summary>対象の選択肢です。非公開のツールも現在値として選択肢に含めます。</summary>
    public IReadOnlyList<InquiryTargetOption> Targets { get; init; } = [];

    /// <summary>担当者の選択肢です。有効なシステム管理者だけを表示します。</summary>
    public IReadOnlyList<UserOption> Assignees { get; init; } = [];

    /// <summary>操作後にだけ表示する処理結果です。</summary>
    public OperationMessage? Message { get; set; }
}

/// <summary>A005 問い合わせ管理の詳細画面の表示情報です。</summary>
public sealed class InquiryDetailViewModel
{
    /// <summary>参照表示する受付内容です。</summary>
    public InquiryDetailView Detail { get; init; } = null!;

    /// <summary>再表示時も維持する更新内容です。</summary>
    public InquiryEditRow Input { get; set; } = new();

    /// <summary>カテゴリの選択肢です。</summary>
    public IReadOnlyList<CodeOption> Categories { get; init; } = [];

    /// <summary>ステータスの選択肢です。</summary>
    public IReadOnlyList<CodeOption> Statuses { get; init; } = [];

    /// <summary>対象の選択肢です。</summary>
    public IReadOnlyList<InquiryTargetOption> Targets { get; init; } = [];

    /// <summary>担当者の選択肢です。</summary>
    public IReadOnlyList<UserOption> Assignees { get; init; } = [];

    /// <summary>操作後にだけ表示する処理結果です。</summary>
    public OperationMessage? Message { get; set; }
}
