using Microsoft.AspNetCore.Mvc.ViewFeatures;
using SalesSupport.Common.UI;

namespace SalesSupport.Portal.Web.Models;

/// <summary>画面間で共通の文言と、保存後リダイレクトをまたぐ画面内メッセージの受け渡しをまとめます。</summary>
/// <remarks>入力本文・秘密情報は保持せず、画面へ表示する区分と文言だけを扱います。</remarks>
public static class PortalMessages
{
    private const string Key = "SalesSupport.Portal.Message";
    private const char Separator = '|';

    /// <summary>同時更新を検出したときに画面へ表示する共通の文言です。画面ごとに書き写しません。</summary>
    /// <remarks>文言は[画面・機能設計]の管理画面共通の同時編集時の動作に従います。</remarks>
    public const string ConflictText = "他の操作で更新されています。再読み込みして最新の内容を確認してください。";

    /// <summary>同時更新を検出したときの画面内メッセージです。</summary>
    public static OperationMessage Conflict { get; } = new(OperationMessageKind.Conflict, ConflictText);

    /// <summary>同時更新を検出したときにAPIが返す表題です。</summary>
    public const string ApiConflictTitle = "更新が競合しました。";

    /// <summary>同時更新を検出したときにAPIが返す説明です。画面と違い、再読み込みではなく最新の内容の再取得を促します。</summary>
    public const string ApiConflictDetail = "他の操作で更新されています。最新の内容を取得してからやり直してください。";

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
