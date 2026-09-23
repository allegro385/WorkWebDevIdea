using System.ComponentModel.DataAnnotations;
using SalesSupport.SampleEstimate.Web.Data;
using SalesSupport.SampleEstimate.Web.Services;

namespace SalesSupport.SampleEstimate.Web.Models;

/// <summary>本人の案件一覧と検索条件を一画面にまとめます。</summary>
public sealed class RecordListViewModel
{
    /// <summary>検索開始日です。</summary>
    public DateOnly? From { get; init; }
    /// <summary>検索終了日です。</summary>
    public DateOnly? To { get; init; }
    /// <summary>区分コードです。</summary>
    public string? CategoryCode { get; init; }
    /// <summary>区分の選択肢です。</summary>
    public IReadOnlyList<EstimateCategory> Categories { get; init; } = [];
    /// <summary>一致した全件の集計と最新100件です。</summary>
    public EstimateRecordList Result { get; init; } = new([], 0, 0);
}

/// <summary>案件編集画面の入力と取得時の更新回数です。</summary>
public sealed class RecordEditViewModel
{
    /// <summary>更新する案件です。</summary>
    public Guid RecordId { get; set; }
    /// <summary>取得時の更新回数を往復させます。</summary>
    public int UpdateCount { get; set; }
    /// <summary>再計算する入力です。</summary>
    public EstimateInput Input { get; set; } = new();
    /// <summary>区分の選択肢です。</summary>
    public IReadOnlyList<EstimateCategory> Categories { get; set; } = [];

    /// <summary>DB行をフォームへ移します。出力方法と送信IDは編集には使いません。</summary>
    public static RecordEditViewModel FromRecord(EstimateRecord record, IReadOnlyList<EstimateCategory> categories) => new()
    {
        RecordId = record.RecordId,
        UpdateCount = record.UpdateCount,
        Categories = categories,
        Input = new EstimateInput
        {
            ProjectName = record.ProjectName, AppliedOn = record.AppliedOn, CategoryCode = record.CategoryCode,
            Quantity = record.Quantity, UnitPrice = record.UnitPrice, Output = EstimateOutput.Screen
        }
    };
}
