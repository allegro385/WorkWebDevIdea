using System.Xml.Linq;
using Xunit;

namespace SalesSupport.Template.Tests;

/// <summary>差し替え前提の題材が残ったまま配置されないための、検査の接続先と骨組みの独立性を確認します。</summary>
/// <remarks>生成したツールはテストプロジェクトを持たないため、テンプレート側のこのテストで契約を固定します。</remarks>
public sealed class TemplateExampleGateTests
{
    private static readonly string SalesSupportRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string ProjectFolder = Path.GetFullPath(Path.Combine(SalesSupportRoot, "src/Template/SalesSupport.Template.Web"));

    /// <summary>題材の名前空間を骨組みの`_ViewImports.cshtml`へ書かず、題材を削除しても骨組みが壊れないことを確認します。</summary>
    [Fact]
    public void ViewImportsDoesNotDependOnExample()
    {
        var viewImports = File.ReadAllText(Path.Combine(ProjectFolder, "Views", "_ViewImports.cshtml"));

        Assert.DoesNotContain("TemplateExample", viewImports, StringComparison.Ordinal);
    }

    /// <summary>検査をコンパイル前と発行前の両方へ接続し、ビルドを省略する発行経路も塞ぐことを確認します。</summary>
    [Fact]
    public void GateRunsBeforeCompileAndOnPublish()
    {
        var project = XDocument.Load(Path.Combine(ProjectFolder, "SalesSupport.Template.Web.csproj"));
        var target = project.Descendants("Target").Single(x => (string?)x.Attribute("Name") == "CheckTemplateExample");
        var hooks = ((string?)target.Attribute("BeforeTargets") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Contains("BeforeCompile", hooks);
        Assert.Contains("Publish", hooks);
    }
}
