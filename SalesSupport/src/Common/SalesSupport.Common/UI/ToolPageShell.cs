using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;

namespace SalesSupport.Common.UI;

/// <summary>Webツール内の画面へ渡す共通レイアウト情報を組み立てます。</summary>
public interface IToolPageShell
{
    /// <summary>ツール内画面の表示情報を返します。</summary>
    /// <param name="title">画面の見出しです。パンくずの末尾にも使います。</param>
    /// <param name="withinTool">ツール内の中間階層です。中間階層がない画面では省略します。</param>
    /// <returns>共通レイアウトへ渡す表示情報です。</returns>
    PageShellModel Build(string title, params Breadcrumb[] withinTool);
}

/// <summary>Portalのトップ・ツール一覧を上位に置き、設定のアプリ名を対象ツール名として付けます。</summary>
/// <remarks>Portal側の画面名とパスはこのクラスだけが持ちます。単独開発ではPortal向けの項目を共通の部品がリンクにせず文字だけで表示します。</remarks>
public sealed class ToolPageShell(IOptions<CommonOptions> options) : IToolPageShell
{
    /// <summary>どのツールでも共通の、Portal側の上位2階層です。</summary>
    private static readonly Breadcrumb[] PortalBreadcrumbs =
    [
        new("トップ", "", LinkTarget.Portal),
        new("ツール一覧", "tools", LinkTarget.Portal)
    ];

    /// <summary>Portal側の項目、ツール内の中間階層、現在の画面の順でパンくずを並べます。</summary>
    /// <param name="title">画面の見出しです。</param>
    /// <param name="withinTool">ツール内の中間階層です。</param>
    /// <returns>共通レイアウトへ渡す表示情報です。</returns>
    public PageShellModel Build(string title, params Breadcrumb[] withinTool) => new()
    {
        PageTitle = title,
        CurrentToolName = options.Value.ApplicationName,
        Breadcrumbs = [.. PortalBreadcrumbs, .. withinTool, new Breadcrumb(title)]
    };
}
