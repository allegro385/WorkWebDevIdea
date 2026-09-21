using System.ComponentModel.DataAnnotations;
using SalesSupport.Common.UI;

namespace SalesSupport.Portal.Web.Models;

/// <summary>P006 個人設定の入力です。本人の2項目と取得時のUpdateCountだけを受け取ります。</summary>
public sealed class PreferenceInput
{
    [Display(Name = "システムのお知らせ")]
    public bool SystemNoticeMailEnabled { get; set; }

    [Display(Name = "お気に入りツールのお知らせ")]
    public bool FavoriteToolNoticeMailEnabled { get; set; }

    /// <summary>取得時点のUpdateCountです。保存時の競合検出に使用します。</summary>
    public int UpdateCount { get; set; }
}

/// <summary>P006 個人設定の表示情報です。</summary>
public sealed class PreferenceViewModel
{
    /// <summary>再表示時も維持する入力です。</summary>
    public PreferenceInput Input { get; init; } = new();

    /// <summary>操作後にだけ表示する処理結果です。常設のお知らせ欄は設けません。</summary>
    public OperationMessage? Message { get; init; }
}
