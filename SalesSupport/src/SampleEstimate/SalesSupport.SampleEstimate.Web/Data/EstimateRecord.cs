using SalesSupport.Common.Entities;

namespace SalesSupport.SampleEstimate.Web.Data;

/// <summary>開発用の見積案件1件です。入力ファイルとTSV本体は保存しません。</summary>
public sealed class EstimateRecord : AuditedEntity
{
    /// <summary>案件の識別子です。</summary>
    public Guid RecordId { get; set; }
    /// <summary>同じ入力フォームの再送信を識別する値です。</summary>
    public Guid SubmissionId { get; set; }
    /// <summary>案件を登録した利用者です。</summary>
    public Guid OwnerUserId { get; set; }
    /// <summary>案件名です。</summary>
    public string ProjectName { get; set; } = "";
    /// <summary>見積の適用日です。</summary>
    public DateOnly AppliedOn { get; set; }
    /// <summary>割引区分のコードです。</summary>
    public string CategoryCode { get; set; } = "";
    /// <summary>数量です。</summary>
    public int Quantity { get; set; }
    /// <summary>円単位の単価です。</summary>
    public int UnitPrice { get; set; }
    /// <summary>円単位の小計です。</summary>
    public decimal Subtotal { get; set; }
    /// <summary>円単位の割引額です。</summary>
    public decimal DiscountAmount { get; set; }
    /// <summary>円単位の合計です。</summary>
    public decimal Total { get; set; }
    /// <summary>入力ファイルの非空行数です。ファイル本体は保存しません。</summary>
    public int? DetailRowCount { get; set; }
}
