using Microsoft.AspNetCore.Mvc.ViewFeatures;
using SalesSupport.Common.UI;

namespace SalesSupport.Portal.Web.Models;

/// <summary>保存後リダイレクトをまたいで画面内メッセージを引き継ぎます。</summary>
/// <remarks>入力本文・秘密情報は保持せず、画面へ表示する区分と文言だけを扱います。</remarks>
public static class PortalMessages
{
    private const string Key = "SalesSupport.Portal.Message";
    private const char Separator = '|';

    /// <summary>次の画面で一度だけ表示するメッセージを設定します。</summary>
    /// <param name="tempData">対象要求のTempDataです。</param>
    /// <param name="kind">成功・警告・競合・失敗の区分です。</param>
    /// <param name="text">画面へ表示する日本語の文言です。</param>
    public static void Set(ITempDataDictionary tempData, OperationMessageKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        tempData[Key] = kind + Separator.ToString() + text;
    }

    /// <summary>設定されているメッセージを取り出します。未設定・形式不正ではnullを返します。</summary>
    public static OperationMessage? Take(ITempDataDictionary tempData)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        if (tempData[Key] is not string stored) return null;
        var separator = stored.IndexOf(Separator);
        if (separator <= 0 || !Enum.TryParse<OperationMessageKind>(stored[..separator], out var kind)) return null;
        return new OperationMessage(kind, stored[(separator + 1)..]);
    }
}
