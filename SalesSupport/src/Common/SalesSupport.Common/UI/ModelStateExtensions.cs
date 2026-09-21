using Microsoft.AspNetCore.Mvc.ModelBinding;
using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.UI;

/// <summary>共通検証の結果を画面の検証表示へ反映します。</summary>
public static class ModelStateExtensions
{
    /// <summary>項目エラーを対応する入力項目へ追加します。</summary>
    public static ModelStateDictionary AddFieldError(this ModelStateDictionary modelState, FieldError error)
    {
        modelState.AddModelError(error.Field, error.Message);
        return modelState;
    }

    /// <summary>検証結果のすべての項目エラーを追加します。妥当な結果では何もしません。</summary>
    public static ModelStateDictionary AddValidationResult(this ModelStateDictionary modelState, ValidationResult result)
    {
        foreach (var error in result.Errors) modelState.AddFieldError(error);
        return modelState;
    }
}
