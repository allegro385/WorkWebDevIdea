using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;

namespace SalesSupport.Common.UI;

/// <summary>Webツール内の画面へ渡す共通レイアウト情報を組み立てます。</summary>
public interface IToolPageShell
{
    /// <summary>ツール内画面の表示情報を返します。見出しは常にツール名です。</summary>
    /// <param name="screenName">入口から一段下がる画面の名前です。入口や結果の画面では省略し、パンくずの末尾をツール名にします。</param>
    /// <returns>共通レイアウトへ渡す表示情報です。</returns>
    PageShellModel Build(string? screenName = null);
}

/// <summary>Portalのトップ・ツール一覧を上位に置き、設定のアプリ名をツール名として見出しとパンくずへ使います。</summary>
/// <remarks>Portal側の画面名とパスはこのクラスだけが持ちます。単独開発ではPortal向けの項目を共通の部品がリンクにせず文字だけで表示します。</remarks>
public sealed class ToolPageShell(IOptions<CommonOptions> options) : IToolPageShell
{
    /// <summary>どのツールでも共通の、Portal側の上位2階層です。</summary>
    private static readonly Breadcrumb[] PortalBreadcrumbs =
    [
        new("トップ", "", LinkTarget.Portal),
        new("ツール一覧", "tools", LinkTarget.Portal)
    ];

    /// <summary>Portal側の項目、ツール名、下層の画面名の順でパンくずを並べ、Portalと同じタイトル帯で表示します。</summary>
    /// <param name="screenName">入口から一段下がる画面の名前です。省略するとパンくずの末尾はツール名です。</param>
    /// <returns>共通レイアウトへ渡す表示情報です。</returns>
    public PageShellModel Build(string? screenName = null)
    {
        var toolName = options.Value.ApplicationName;
        return new()
        {
            PageTitle = toolName,
            DocumentTitle = screenName is null ? null : $"{screenName} - {toolName}",
            UseTitleBand = true,
            Breadcrumbs = screenName is null
                ? [.. PortalBreadcrumbs, new Breadcrumb(toolName)]
                : [.. PortalBreadcrumbs, new Breadcrumb(toolName, "", LinkTarget.Local), new Breadcrumb(screenName)]
        };
    }
}
