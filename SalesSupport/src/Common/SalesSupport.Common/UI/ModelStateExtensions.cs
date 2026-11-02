using Microsoft.AspNetCore.Mvc.ModelBinding;
using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.UI;

/// <summary>共通検証の結果を画面の検証表示へ反映します。</summary>
public static class ModelStateExtensions
{
    /// <summary>検証結果のすべての項目エラーを、画面の入力欄と同じキーで追加します。妥当な結果では何もしません。</summary>
    /// <param name="modelState">追加先のModelStateです。</param>
    /// <param name="result">Serviceの検証結果です。項目名は入力モデルのプロパティ名です。</param>
    /// <param name="prefix">入力モデルを持つ表示情報のプロパティ名です（例：<c>Input</c>）。項目名を<c>Input.Quantity</c>の形にし、<c>asp-validation-for</c>と一致させます。NULLは項目名のまま追加します。</param>
    /// <param name="separateFields">入力モデルとは別の引数で受け取る項目（ファイル選択など）の名前です。接頭辞を付けずに追加します。</param>
    /// <returns>追加後のModelStateです。</returns>
    public static ModelStateDictionary AddValidationResult(this ModelStateDictionary modelState, ValidationResult result, string? prefix = null, params string[] separateFields)
    {
        foreach (var error in result.Errors)
        {
            var key = separateFields.Contains(error.Field) ? error.Field : ModelNames.CreatePropertyModelName(prefix, error.Field);
            modelState.AddModelError(key, error.Message);
        }
        return modelState;
    }
}
