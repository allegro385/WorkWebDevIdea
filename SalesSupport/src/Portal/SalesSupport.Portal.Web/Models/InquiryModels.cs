using System.ComponentModel.DataAnnotations;
using SalesSupport.Common.MasterData;
using SalesSupport.Common.UI;

namespace SalesSupport.Portal.Web.Models;

/// <summary>問い合わせの対象を表す選択肢です。</summary>
/// <param name="Value">画面から受け取る値です。ツールは`TOOL:`とToolIdで表します。</param>
/// <param name="Label">画面へ表示する名称です。</param>
public sealed record InquiryTargetOption(string Value, string Label);

/// <summary>P007 問い合わせ・ご意見の入力です。送信者名とメールアドレスは入力しません。</summary>
public sealed class InquiryInput
{
    [Display(Name = "カテゴリ")]
    [Required(ErrorMessage = "カテゴリを選択してください。")]
    public string? CategoryCode { get; set; }

    [Display(Name = "対象")]
    [Required(ErrorMessage = "対象を選択してください。")]
    public string? Target { get; set; }

    [Display(Name = "内容")]
    [Required(ErrorMessage = "内容を入力してください。")]
    [StringLength(2000, ErrorMessage = "内容は2,000文字以内で入力してください。")]
    public string? Content { get; set; }

    /// <summary>本人へ束縛した一回限りの送信IDです。二重送信の抑止に使用します。</summary>
    public Guid SubmissionId { get; set; }
}

/// <summary>P007 問い合わせ・ご意見の表示情報です。</summary>
public sealed class InquiryViewModel
{
    /// <summary>再表示時も維持する入力です。ファイル選択は復元しません。</summary>
    public InquiryInput Input { get; init; } = new();

    /// <summary>汎用コードマスタから取得したカテゴリの選択肢です。</summary>
    public IReadOnlyList<CodeOption> Categories { get; init; } = [];

    /// <summary>ポータルサイト、利用できるツールおよびその他で構成した対象の選択肢です。</summary>
    public IReadOnlyList<InquiryTargetOption> Targets { get; init; } = [];

    /// <summary>添付ファイルの許可拡張子と容量上限の案内文です。</summary>
    public string AttachmentHint { get; init; } = "";

    /// <summary>操作後にだけ表示する処理結果です。</summary>
    public OperationMessage? Message { get; init; }
}

/// <summary>受付完了画面の表示情報です。</summary>
/// <param name="InquiryId">受付した問い合わせIDです。</param>
public sealed record InquiryCompletedViewModel(string InquiryId);
