using System.ComponentModel.DataAnnotations;
using SalesSupport.Common.UI;

namespace SalesSupport.SampleEstimate.Web.Models;

/// <summary>計算結果の表示、直接出力、要約保存を選びます。</summary>
public enum EstimateOutput
{
    /// <summary>実行応答で結果画面を表示します。</summary>
    Screen,
    /// <summary>実行応答でTSVを直接ダウンロードします。</summary>
    Download,
    /// <summary>計算した要約を案件として保存します。入力ファイルは保存しません。</summary>
    Save
}

/// <summary>ドロップダウンの選択肢1件です。</summary>
/// <param name="Code">画面から受け取る値です。</param>
/// <param name="Name">画面へ表示する名称です。</param>
/// <param name="DiscountRate">小計へ適用する割引率です。0.1は10%を表します。</param>
/// <remarks>サンプルでは固定値です。他のツールでは業務仕様に応じてコードマスタなどへ変更します。</remarks>
public sealed record EstimateCategory(string Code, string Name, decimal DiscountRate);

/// <summary>入力画面の項目です。ツールの題材にあわせて差し替える例であり、共通契約ではありません。</summary>
public sealed class EstimateInput
{
    /// <summary>同じフォームの再送信を識別します。保存時に必須です。</summary>
    public Guid SubmissionId { get; set; }
    /// <summary>文字列入力の例です。</summary>
    [Display(Name = "案件名")]
    [Required(ErrorMessage = "案件名を入力してください。")]
    [StringLength(60, ErrorMessage = "案件名は60文字以内で入力してください。")]
    public string? ProjectName { get; set; }

    /// <summary>数値入力の例です。入力欄の属性だけに頼らず、Serviceでも範囲を再検証します。</summary>
    [Display(Name = "数量")]
    [Required(ErrorMessage = "数量を入力してください。")]
    [Range(1, 9999, ErrorMessage = "数量は1以上9,999以下で入力してください。")]
    public int? Quantity { get; set; }

    /// <summary>金額入力の例です。単位は円で、小数は受け付けません。</summary>
    [Display(Name = "単価")]
    [Required(ErrorMessage = "単価を入力してください。")]
    [Range(0, 9_999_999, ErrorMessage = "単価は0以上9,999,999以下で入力してください。")]
    public int? UnitPrice { get; set; }

    /// <summary>日付入力の例です。初期値には業務日付を表示します。</summary>
    [Display(Name = "適用日")]
    [Required(ErrorMessage = "適用日を入力してください。")]
    [DataType(DataType.Date)]
    public DateOnly? AppliedOn { get; set; }

    /// <summary>ドロップダウン入力の例です。</summary>
    [Display(Name = "区分")]
    [Required(ErrorMessage = "区分を選択してください。")]
    public string? CategoryCode { get; set; }

    /// <summary>ラジオボタン入力の例です。結果の受け取り方を選びます。</summary>
    [Display(Name = "出力方法")]
    public EstimateOutput Output { get; set; } = EstimateOutput.Screen;
}

/// <summary>入力画面の表示情報です。再表示でもファイル選択は復元しません。</summary>
public sealed class EstimateViewModel
{
    /// <summary>コードマスタに基づく現在のツール公開状態です。</summary>
    public string ToolStatusName { get; init; } = "";
    /// <summary>再表示時も維持する入力です。</summary>
    public EstimateInput Input { get; init; } = new();

    /// <summary>区分の選択肢です。</summary>
    public IReadOnlyList<EstimateCategory> Categories { get; init; } = [];

    /// <summary>明細ファイルの許可拡張子と容量上限の案内文です。</summary>
    public string DetailFileHint { get; init; } = "";

    /// <summary>操作後にだけ表示する処理結果です。</summary>
    public OperationMessage? Message { get; init; }
}

/// <summary>計算結果です。入力値と算出値だけを保持し、ファイルの内容は保持しません。</summary>
/// <param name="ProjectName">入力された案件名です。</param>
/// <param name="AppliedOn">入力された適用日です。</param>
/// <param name="CategoryName">選択された区分の表示名です。</param>
/// <param name="Quantity">入力された数量です。</param>
/// <param name="UnitPrice">入力された単価です。単位は円です。</param>
/// <param name="Subtotal">数量と単価から求めた小計です。単位は円です。</param>
/// <param name="DiscountAmount">区分の割引率から求めた割引額です。単位は円です。</param>
/// <param name="Total">小計から割引額を差し引いた合計です。単位は円です。</param>
/// <param name="DetailRowCount">明細ファイルの行数です。ファイルを選択しなかった場合はNULLです。</param>
public sealed record EstimateResult(string ProjectName, DateOnly AppliedOn, string CategoryName, int Quantity, int UnitPrice,
    decimal Subtotal, decimal DiscountAmount, decimal Total, int? DetailRowCount);
