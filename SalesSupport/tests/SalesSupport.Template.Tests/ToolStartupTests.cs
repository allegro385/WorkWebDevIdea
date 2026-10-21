using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Template.Web.Bootstrap;
using Xunit;

namespace SalesSupport.Template.Tests;

/// <summary>生成直後のF5（<c>standalone</c>起動プロファイル）がPortal・共通設定なしで起動でき、<c>portal</c>起動プロファイルがローカルの共通設定を指すことを確認します。</summary>
public sealed class ToolStartupTests
{
    private static readonly string SalesSupportRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string ProjectFolder = Path.GetFullPath(Path.Combine(SalesSupportRoot, "src/Template/SalesSupport.Template.Web"));

    /// <summary>既定（先頭）の起動プロファイルは単独開発で、連携用のプロファイルはリポジトリのローカル共通設定を指します。</summary>
    [Fact]
    public void LaunchProfilesSeparateStandaloneAndPortal()
    {
        var profiles = Profiles();
        var first = profiles.EnumerateObject().First();
        Assert.Equal("standalone", first.Name);
        Assert.Equal("Standalone", Variables(first.Value)["SalesSupport__Tool__ExecutionMode"]);

        var portal = Variables(profiles.GetProperty("portal"));
        Assert.Equal("Portal", portal["SalesSupport__Tool__ExecutionMode"]);
        Assert.Equal(Path.Combine(SalesSupportRoot, "config", "salessupport.common.Development.json"),
            Path.GetFullPath(portal["SalesSupport__CommonConfigPath"]!, ProjectFolder));
    }

    /// <summary>外側のStandalone設定をportalプロファイルで上書きし、共通設定がなければ単独開発へ切り替えず起動を拒否します。</summary>
    [Fact]
    public void PortalProfileOverridesStandaloneAndRequiresCommonConfiguration()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "ss-portal-" + Guid.NewGuid().ToString("N"), "src", "tool", "web");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ToolExecutionModeSettings.Key] = "Standalone" })
            .AddInMemoryCollection(Variables(Profiles().GetProperty("portal"))
                .Select(x => KeyValuePair.Create(x.Key.Replace("__", ":"), x.Value))).Build();
        var services = new ServiceCollection().AddLogging();
        var exception = Assert.Throws<ConfigurationException>(() =>
            services.AddSalesSupportTool(new StubEnvironment { ContentRootPath = contentRoot }, configuration));
        Assert.Contains(CommonConfiguration.PathKey, exception.Message);
    }

    /// <summary>単独開発の起動プロファイルの設定だけで、共通設定ファイル・鍵・保存領域のないフォルダーから起動時の設定検証を通ります。CSRF検証も既定で適用します。</summary>
    [Fact]
    public void StandaloneProfileStartsWithoutCommonConfiguration()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "ss-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                Variables(Profiles().GetProperty("standalone")).Select(x => KeyValuePair.Create(x.Key.Replace("__", ":"), x.Value))).Build();
            var services = new ServiceCollection().AddLogging();
            services.AddSalesSupportTool(new StubEnvironment { ContentRootPath = contentRoot }, configuration);
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IStartupValidator>().Validate();
            Assert.True(provider.GetRequiredService<IOptions<CommonOptions>>().Value.IsStandalone);
            Assert.Contains(provider.GetRequiredService<IOptions<MvcOptions>>().Value.Filters, x => x is AutoValidateAntiforgeryTokenAttribute);
        }
        finally { Directory.Delete(contentRoot, recursive: true); }
    }

    /// <summary>テンプレートの起動プロファイル一覧を読み込みます。</summary>
    /// <returns><c>profiles</c>要素です。</returns>
    private static JsonElement Profiles() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectFolder, "Properties", "launchSettings.json"))).RootElement.GetProperty("profiles").Clone();

    /// <summary>起動プロファイルの環境変数を取り出します。</summary>
    /// <param name="profile">起動プロファイルです。</param>
    /// <returns>環境変数名と値です。</returns>
    private static Dictionary<string, string?> Variables(JsonElement profile) =>
        profile.GetProperty("environmentVariables").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString());

    /// <summary>起動プロファイルと同じ開発環境のホスト情報です。</summary>
    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
