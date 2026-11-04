using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.UI;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>ツール内画面の共通レイアウト情報を、Portal側のパンくずごとCommonが組み立てることを確認します。</summary>
public sealed class ToolPageShellTests
{
    /// <summary>画面名を省略すると、見出しとパンくずの末尾がツール名になり、Portalと同じタイトル帯で表示します。</summary>
    [Fact]
    public void UsesToolNameWhenScreenNameIsOmitted()
    {
        var shell = new ToolPageShell(Options.Create(new CommonOptions { ApplicationName = "見積試算" }));

        var model = shell.Build();

        Assert.Equal("見積試算", model.PageTitle);
        Assert.Null(model.DocumentTitle);
        Assert.Null(model.CurrentToolName);
        Assert.True(model.UseTitleBand);
        Breadcrumb[] expected =
        [
            new("トップ", "", LinkTarget.Portal),
            new("ツール一覧", "tools", LinkTarget.Portal),
            new("見積試算")
        ];
        Assert.Equal(expected, model.Breadcrumbs);
    }

    /// <summary>下層画面でも見出しはツール名のままとし、ツール名を入口へのリンクにして画面名を続けます。</summary>
    [Fact]
    public void KeepsToolNameHeadingOnScreenWithinTool()
    {
        var shell = new ToolPageShell(Options.Create(new CommonOptions { ApplicationName = "見積試算" }));

        var model = shell.Build("案件の編集");

        Assert.Equal("見積試算", model.PageTitle);
        Assert.Equal("案件の編集 - 見積試算", model.DocumentTitle);
        Assert.True(model.UseTitleBand);
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
