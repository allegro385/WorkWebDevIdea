using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DependencyInjection;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>サイトの通信方式（HTTPS・HTTP）の受付条件と、HTTP運用でのCookieのSecure条件・Portal URLのスキーム検証を確認します。</summary>
[Collection(CommonConfigPathCollection.Name)]
public sealed class SiteTransportModeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ss-transport-" + Guid.NewGuid().ToString("N"));
    private readonly string contentRoot;

    /// <summary>標準配置（<c>apps/portal</c>と<c>config</c>・<c>keys</c>）を模した検証用フォルダーを作成します。</summary>
    public SiteTransportModeTests()
    {
        contentRoot = Path.Combine(root, "apps", "portal");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "keys"));
    }

    /// <summary>検証用フォルダーを削除します。</summary>
    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { /* 検証環境の残存は無視します。 */ }
    }

    /// <summary>未設定はHTTPSとし、定義済みの値以外は構成エラーにします。大小文字の揺れを受け付けません。</summary>
    /// <param name="value">共通設定ファイルの値です。</param>
    /// <param name="expected">期待する通信方式です。NULLは構成エラーを期待します。</param>
    [Theory]
    [InlineData(null, SiteTransportMode.Https)]
    [InlineData("Https", SiteTransportMode.Https)]
    [InlineData("Http", SiteTransportMode.Http)]
    [InlineData("http", null)]
    [InlineData("HTTP", null)]
    [InlineData("None", null)]
    [InlineData("true", null)]
    [InlineData("", null)]
    public void ReadAcceptsOnlyDefinedModes(string? value, SiteTransportMode? expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [SiteTransportModeSettings.Key] = value }).Build();
        if (expected is null)
        {
            var exception = Assert.Throws<ConfigurationException>(() => SiteTransportModeSettings.Read(configuration));
            Assert.Contains(SiteTransportModeSettings.Key, exception.Message);
            return;
        }
        Assert.Equal(expected, SiteTransportModeSettings.Read(configuration));
    }

    /// <summary>単独開発は共通設定ファイルを読まないため、HTTP運用として扱いません。</summary>
    [Fact]
    public void StandaloneIsNotHttpTransport()
    {
        Assert.True(new CommonOptions
        {
            Kind = ApplicationKind.Portal, EnvironmentCode = "PRODUCTION", TransportMode = SiteTransportMode.Http
        }.IsHttpTransport);
        Assert.False(new CommonOptions
        {
            Kind = ApplicationKind.Tool, EnvironmentCode = "DEVELOPMENT", ExecutionMode = ToolExecutionMode.Standalone,
            TransportMode = SiteTransportMode.Http
        }.IsHttpTransport);
        Assert.False(new CommonOptions { Kind = ApplicationKind.Portal, EnvironmentCode = "PRODUCTION" }.IsHttpTransport);
    }

    /// <summary>HTTP運用では、共有認証CookieとCSRFのCookieをHTTPSの要求にだけSecureとして送ります。</summary>
    [Fact]
    public void HttpTransportSendsSecureOnlyForHttpsRequests()
    {
        using var provider = Register(SiteTransportMode.Http, "http://example.invalid/");
        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.True(provider.GetRequiredService<IOptions<CommonOptions>>().Value.IsHttpTransport);
        Assert.Equal(CookieSecurePolicy.SameAsRequest,
            provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(SharedCookieContract.Scheme).Cookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.SecurePolicy);
    }

    /// <summary>既定のHTTPSでは、Cookieへ常にSecureを付けます。</summary>
    [Fact]
    public void HttpsTransportAlwaysRequiresSecureCookies()
    {
        using var provider = Register(SiteTransportMode.Https, "https://example.invalid/");
        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.False(provider.GetRequiredService<IOptions<CommonOptions>>().Value.IsHttpTransport);
        Assert.Equal(CookieSecurePolicy.Always,
            provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(SharedCookieContract.Scheme).Cookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.Always, provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.SecurePolicy);
    }

    /// <summary>Portal URLのスキームが通信方式と一致しない設定は、起動時の構成エラーにします。</summary>
    /// <param name="mode">共通設定ファイルの通信方式です。</param>
    /// <param name="baseUrl">共通設定ファイルのPortal URLです。</param>
    [Theory]
    [InlineData(SiteTransportMode.Https, "http://example.invalid/")]
    [InlineData(SiteTransportMode.Http, "https://example.invalid/")]
    public void BaseUrlSchemeMustMatchTransportMode(SiteTransportMode mode, string baseUrl)
    {
        using var provider = Register(mode, baseUrl);

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CommonOptions>>().Value);

        Assert.Contains("Portal:BaseUrl", exception.Message);
    }

    /// <summary>指定した通信方式とPortal URLの共通設定ファイルを作成し、PortalのCommonを登録します。</summary>
    /// <param name="mode">共通設定ファイルへ書き込む通信方式です。</param>
    /// <param name="baseUrl">共通設定ファイルへ書き込むPortalのURLです。</param>
    /// <returns>登録済みのサービスです。</returns>
    private ServiceProvider Register(SiteTransportMode mode, string baseUrl)
    {
        var settings = new
        {
            ConnectionStrings = new { SalesSupport = "Data Source=verify;" },
            SalesSupport = new
            {
                EnvironmentCode = "DEVELOPMENT",
                Site = new { Name = "検証サイト", TransportMode = mode.ToString() },
                Application = new { Name = "検証ポータル" },
                Portal = new { BaseUrl = baseUrl },
                DataProtection = new { KeyDirectory = "../keys" },
                Mail = new
                {
                    Host = "127.0.0.1", Port = 2525, TlsMode = "StartTls", From = "noreply@example.invalid",
                    DevelopmentRecipient = "dev@example.invalid", MaxRecipients = 50
                }
            }
        };
        File.WriteAllText(Path.Combine(root, "config", "salessupport.common.json"), System.Text.Json.JsonSerializer.Serialize(settings));
        var environment = new StubEnvironment { ContentRootPath = contentRoot, WebRootPath = Path.Combine(contentRoot, "wwwroot") };
        var services = new ServiceCollection().AddLogging();
        // 保存領域の検証がWeb公開領域を参照するため、ホストと同じ環境情報を登録します。
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddSalesSupportCommon(ApplicationKind.Portal, environment, new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    /// <summary>起動環境名、コンテンツルートおよびWeb公開領域だけを指定するホスト情報です。</summary>
    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        // MVCは起動アセンブリ名から部品を読み込むため、この試験アセンブリ名を与えます。
        public string ApplicationName { get; set; } = typeof(SiteTransportModeTests).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
