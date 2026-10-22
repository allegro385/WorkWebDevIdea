using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.UI;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>ツール内画面の共通レイアウト情報を、Portal側のパンくずごとCommonが組み立てることを確認します。</summary>
public sealed class ToolPageShellTests
{
    /// <summary>中間階層のない画面では、Portalのトップ・ツール一覧に続けて画面名だけを並べます。</summary>
    [Fact]
    public void BuildsPortalBreadcrumbsAndToolName()
    {
        var shell = new ToolPageShell(Options.Create(new CommonOptions { ApplicationName = "見積試算" }));

        var model = shell.Build("入力");

        Assert.Equal("入力", model.PageTitle);
        Assert.Equal("見積試算", model.CurrentToolName);
        Breadcrumb[] expected =
        [
            new("トップ", "", LinkTarget.Portal),
            new("ツール一覧", "tools", LinkTarget.Portal),
            new("入力")
        ];
        Assert.Equal(expected, model.Breadcrumbs);
    }

    /// <summary>ツール内に中間階層がある画面では、Portal側の項目の後ろへ渡された順で差し込みます。</summary>
    [Fact]
    public void InsertsBreadcrumbsWithinTool()
    {
        var shell = new ToolPageShell(Options.Create(new CommonOptions { ApplicationName = "見積試算" }));

        var model = shell.Build("案件の編集", new Breadcrumb("見積試算", "", LinkTarget.Local));

        Breadcrumb[] expected =
        [
            new("トップ", "", LinkTarget.Portal),
            new("ツール一覧", "tools", LinkTarget.Portal),
            new("見積試算", "", LinkTarget.Local),
            new("案件の編集")
        ];
        Assert.Equal(expected, model.Breadcrumbs);
    }
}
