using System.Text.RegularExpressions;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>共通部品の見た目をCommonの1か所に集約し、Portalとツールの表示差を作らないことを確認します。</summary>
public sealed class SharedStyleTests
{
    private static readonly string SourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src"));

    /// <summary>各アプリのCSSは共通部品を単独のセレクターで上書きしません。アプリ固有の要素内の指定は対象外です。</summary>
    /// <param name="relativePath">srcフォルダーからのCSSファイルの相対パスです。</param>
    [Theory]
    [InlineData("Portal/SalesSupport.Portal.Web/wwwroot/css/site.css")]
    [InlineData("Template/SalesSupport.Template.Web/wwwroot/css/site.css")]
    [InlineData("SampleEstimate/SalesSupport.SampleEstimate.Web/wwwroot/css/site.css")]
    public void ApplicationStylesDoNotOverrideSharedParts(string relativePath)
    {
        var css = File.ReadAllText(Path.Combine(SourceRoot, relativePath));

        Assert.Empty(Regex.Matches(css, @"^\s*\.ss-[\w-]+", RegexOptions.Multiline));
    }

    /// <summary>開発環境の帯は指定色の単色で、Commonだけが定義します。</summary>
    [Fact]
    public void DevelopmentBandUsesSpecifiedColor()
    {
        var css = File.ReadAllText(Path.Combine(SourceRoot, "Common/SalesSupport.Common/wwwroot/css/sales-support.css"));

        var rule = Regex.Match(css, @"\.ss-header-development\s*\{(?<body>[^}]*)\}").Groups["body"].Value;
        Assert.Contains("--ss-band-start: #dc3545;", rule);
        Assert.Contains("--ss-band-end: #dc3545;", rule);
    }
}
