using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;

namespace SalesSupport.Common.UI;

/// <summary>リンクの解決先です。Portalと表示中アプリを区別します。</summary>
public enum LinkTarget { Local, Portal }

/// <summary>パンくず1件です。URLは検証済みのサイト内経路だけを受け付けます。</summary>
public sealed record Breadcrumb(string Label, string? Path = null, LinkTarget Target = LinkTarget.Local);

/// <summary>画面内メッセージの種類です。</summary>
public enum OperationMessageKind { Success, Warning, Conflict, Failure }

/// <summary>保存結果・競合・入力失敗の画面内表示です。</summary>
public sealed record OperationMessage(OperationMessageKind Kind, string Text);

/// <summary>共通レイアウトへ渡す画面情報です。ユーザーや権限は受け取りません。</summary>
public sealed class PageShellModel
{
    /// <summary>ブラウザーのタイトルと画面見出しに使用します。</summary>
    public string PageTitle { get; init; } = "";
    /// <summary>ヘッダー直下へ表示する階層です。</summary>
    public IReadOnlyList<Breadcrumb> Breadcrumbs { get; init; } = [];
    /// <summary>ツール内画面で表示する対象ツール名です。</summary>
    public string? CurrentToolName { get; init; }
    /// <summary>通常機能への共通メニューを表示するかです。認証・入場制限案内の画面ではfalseにします。</summary>
    public bool ShowCommonMenus { get; init; } = true;
}

/// <summary>画面モデルの型に依存せず共通レイアウトへ画面情報を渡します。</summary>
public static class PageShellExtensions
{
    private const string Key = "SalesSupport.PageShell";

    /// <summary>Controllerまたは画面から共通レイアウトの表示情報を設定します。</summary>
    public static Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary SetPageShell(
        this Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary viewData, PageShellModel shell)
    {
        viewData[Key] = shell;
        return viewData;
    }

    /// <summary>未設定でも既定値を返し、レイアウトの描画を止めません。</summary>
    public static PageShellModel GetPageShell(this Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary viewData) =>
        viewData[Key] as PageShellModel ?? new PageShellModel();
}

/// <summary>配置先のサブパスとPortalの公開URLを考慮してリンクを生成します。</summary>
public interface ISalesSupportLinks
{
    /// <summary>表示中アプリ内のリンクをPathBase込みで生成します。</summary>
    string Local(string relativePath = "");
    /// <summary>Portalの公開URLからリンクを生成します。</summary>
    string Portal(string relativePath = "");
    /// <summary>指定された解決先でリンクを生成します。</summary>
    string Resolve(string relativePath, LinkTarget target);
}

/// <summary>任意URL・親移動・スキーム付きの経路を拒否するリンク生成です。</summary>
public sealed class SalesSupportLinks(IOptions<CommonOptions> options, IHttpContextAccessor http) : ISalesSupportLinks
{
    /// <summary>PathBaseを前置し、アプリ内の絶対パスを返します。</summary>
    public string Local(string relativePath = "")
    {
        var pathBase = http.HttpContext?.Request.PathBase.Value ?? "";
        return pathBase.TrimEnd('/') + "/" + Safe(relativePath);
    }

    /// <summary>末尾スラッシュ付きのPortal公開URLへ結合します。</summary>
    public string Portal(string relativePath = "") => options.Value.PortalBaseUrl + Safe(relativePath);

    /// <summary>解決先に応じてLocalまたはPortalのリンクを返します。</summary>
    public string Resolve(string relativePath, LinkTarget target) => target == LinkTarget.Portal ? Portal(relativePath) : Local(relativePath);

    /// <summary>相対経路以外を拒否します。クエリは`?`以降だけ許可します。</summary>
    private static string Safe(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return "";
        if (relativePath[0] == '/' || relativePath.Any(char.IsControl) || relativePath.Contains('\\')
            || relativePath.Contains("..", StringComparison.Ordinal) || relativePath.Contains("//", StringComparison.Ordinal)
            || Uri.TryCreate(relativePath, UriKind.Absolute, out _))
            throw new ArgumentException("リンクの相対経路が不正です。", nameof(relativePath));
        return relativePath;
    }
}
