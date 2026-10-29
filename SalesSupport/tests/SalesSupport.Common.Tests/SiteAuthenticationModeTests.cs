using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Data;
using SalesSupport.Common.DependencyInjection;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>サイトの認証モード（メールOTP・匿名）の受付条件と、匿名モードの固定ユーザー・既定スキーム・境界を確認します。</summary>
[Collection(CommonConfigPathCollection.Name)]
public sealed class SiteAuthenticationModeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ss-auth-" + Guid.NewGuid().ToString("N"));
    private readonly string contentRoot;

    /// <summary>標準配置（<c>apps/portal</c>と<c>config</c>・<c>keys</c>）を模した検証用フォルダーを作成します。</summary>
    public SiteAuthenticationModeTests()
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

    /// <summary>未設定はメールOTPとし、定義済みの値以外は構成エラーにします。大小文字の揺れを受け付けません。</summary>
    /// <param name="value">共通設定ファイルの値です。</param>
    /// <param name="expected">期待する認証モードです。NULLは構成エラーを期待します。</param>
    [Theory]
    [InlineData(null, SiteAuthenticationMode.EmailOtp)]
    [InlineData("EmailOtp", SiteAuthenticationMode.EmailOtp)]
    [InlineData("Anonymous", SiteAuthenticationMode.Anonymous)]
    [InlineData("anonymous", null)]
    [InlineData("ANONYMOUS", null)]
    [InlineData("None", null)]
    [InlineData("true", null)]
    [InlineData("", null)]
    public void ReadAcceptsOnlyDefinedModes(string? value, SiteAuthenticationMode? expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [SiteAuthenticationModeSettings.Key] = value }).Build();
        if (expected is null)
        {
            var exception = Assert.Throws<ConfigurationException>(() => SiteAuthenticationModeSettings.Read(configuration));
            Assert.Contains(SiteAuthenticationModeSettings.Key, exception.Message);
            return;
        }
        Assert.Equal(expected, SiteAuthenticationModeSettings.Read(configuration));
    }

    /// <summary>単独開発は共通設定ファイルを読まないため、匿名モードとして扱いません。</summary>
    [Fact]
    public void StandaloneIsNotAnonymous()
    {
        Assert.True(new CommonOptions
        {
            Kind = ApplicationKind.Portal, EnvironmentCode = "PRODUCTION", AuthenticationMode = SiteAuthenticationMode.Anonymous
        }.IsAnonymous);
        Assert.False(new CommonOptions
        {
            Kind = ApplicationKind.Tool, EnvironmentCode = "DEVELOPMENT", ExecutionMode = ToolExecutionMode.Standalone,
            AuthenticationMode = SiteAuthenticationMode.Anonymous
        }.IsAnonymous);
        Assert.False(new CommonOptions { Kind = ApplicationKind.Portal, EnvironmentCode = "PRODUCTION" }.IsAnonymous);
    }

    /// <summary>匿名モードでは既定スキームを匿名認証にし、ツールの明示ポリシーも同じスキームで満たせるようにします。</summary>
    [Fact]
    public async Task AnonymousModeUsesAnonymousSchemeForDefaultAndToolPolicy()
    {
        await using var provider = Register(SiteAuthenticationMode.Anonymous);
        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.True(provider.GetRequiredService<IOptions<CommonOptions>>().Value.IsAnonymous);
        Assert.Equal(AnonymousSiteAuthenticationHandler.SchemeName,
            (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync())!.Name);
        var authorization = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        var fallback = authorization.FallbackPolicy!;
        Assert.Equal([AnonymousSiteAuthenticationHandler.SchemeName], fallback.AuthenticationSchemes);
        // 固定の共通ユーザーも認証済み利用者として扱うため、既定方針の利用者要求は残します。
        Assert.Contains(fallback.Requirements, x => x is DenyAnonymousAuthorizationRequirement);
        Assert.Equal([AnonymousSiteAuthenticationHandler.SchemeName], authorization.GetPolicy("SalesSupportTool")!.AuthenticationSchemes);
        // 管理者の明示ポリシーは共有Cookieの実ユーザーだけを対象とし、匿名モードでは満たしません。
        Assert.Equal([SharedCookieContract.Scheme], authorization.GetPolicy("SalesSupportAdmin")!.AuthenticationSchemes);
    }

    /// <summary>メールOTPのモードでは共有Cookieを既定スキームとして使います。</summary>
    [Fact]
    public async Task EmailOtpModeKeepsSharedCookieScheme()
    {
        await using var provider = Register(SiteAuthenticationMode.EmailOtp);
        Assert.False(provider.GetRequiredService<IOptions<CommonOptions>>().Value.IsAnonymous);
        Assert.Equal(SharedCookieContract.Scheme,
            (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync())!.Name);
        Assert.Equal([SharedCookieContract.Scheme],
            provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy("SalesSupportTool")!.AuthenticationSchemes);
    }

    /// <summary>匿名モードではメールを使わないため、Portalでも送信を無効へ固定します。</summary>
    [Fact]
    public async Task AnonymousModeDisablesMail()
    {
        await using var anonymous = Register(SiteAuthenticationMode.Anonymous);
        Assert.False(anonymous.GetRequiredService<IOptions<MailOptions>>().Value.Enabled);
        await using var emailOtp = Register(SiteAuthenticationMode.EmailOtp);
        Assert.True(emailOtp.GetRequiredService<IOptions<MailOptions>>().Value.Enabled);
    }

    /// <summary>匿名認証は固定の共通ユーザーを検証済み利用者として確定し、Cookieを読みません。</summary>
    [Fact]
    public async Task AnonymousHandlerConfirmsFixedUser()
    {
        await using var services = new ServiceCollection().AddOptions().AddLogging().BuildServiceProvider();
        var current = new CurrentUserAccessor();
        var handler = new AnonymousSiteAuthenticationHandler(services.GetRequiredService<IOptionsMonitor<AuthenticationSchemeOptions>>(),
            services.GetRequiredService<ILoggerFactory>(), UrlEncoder.Default, current);
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = SharedCookieContract.CookieName + "=invalid";
        await handler.InitializeAsync(
            new AuthenticationScheme(AnonymousSiteAuthenticationHandler.SchemeName, null, typeof(AnonymousSiteAuthenticationHandler)), context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.Principal!.Identity!.IsAuthenticated);
        Assert.Equal(AnonymousSiteUser.UserId.ToString(), result.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(new CurrentUser(AnonymousSiteUser.UserId, AnonymousSiteUser.DisplayName, AnonymousSiteUser.RoleCode), current.User);
    }

    /// <summary>匿名モードでは残っている共有Cookieを受理せず、実ユーザーとして扱いません。</summary>
    [Fact]
    public async Task AnonymousModeRejectsSharedCookie()
    {
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(Guid.NewGuid(), "前回の利用者", "ADMIN"));
        var options = Options.Create(new CommonOptions
        {
            Kind = ApplicationKind.Portal, EnvironmentCode = "PRODUCTION", AuthenticationMode = SiteAuthenticationMode.Anonymous
        });
        var events = new SharedCookieEvents(new RejectFactory(), current,
            new SalesSupport.Common.DateTime.ApplicationClock(TimeProvider.System), options, Options.Create(new IdentityOptions()));
        var scheme = new AuthenticationScheme(SharedCookieContract.Scheme, null, typeof(CookieAuthenticationHandler));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], SharedCookieContract.Scheme));
        var validate = new CookieValidatePrincipalContext(new DefaultHttpContext(), scheme, new CookieAuthenticationOptions(),
            new AuthenticationTicket(principal, new AuthenticationProperties(), SharedCookieContract.Scheme));

        await events.ValidatePrincipal(validate);

        Assert.Null(validate.Principal);
        Assert.Null(current.User);
    }

    /// <summary>匿名モードでも利用判定をDBで行い、単独開発のような認証なしの近道を使いません。</summary>
    [Fact]
    public async Task AnonymousModeStillEvaluatesDatabaseState()
    {
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(AnonymousSiteUser.UserId, AnonymousSiteUser.DisplayName, AnonymousSiteUser.RoleCode));
        var options = Options.Create(new CommonOptions
        {
            Kind = ApplicationKind.Tool, EnvironmentCode = "PRODUCTION", ToolId = "T001", AuthenticationMode = SiteAuthenticationMode.Anonymous
        });
        var evaluator = new AccessEvaluator(current, new RejectSettings(), new RejectFactory(), options);

        await Assert.ThrowsAsync<InvalidOperationException>(() => evaluator.EvaluateAsync(new(AccessPurpose.ToolUse, "T001")));
    }

    /// <summary>指定した認証モードの共通設定ファイルを作成し、PortalのCommonを登録します。</summary>
    /// <param name="mode">共通設定ファイルへ書き込む認証モードです。</param>
    /// <returns>登録済みのサービスです。</returns>
    private ServiceProvider Register(SiteAuthenticationMode mode)
    {
        var settings = new
        {
            ConnectionStrings = new { SalesSupport = "Data Source=verify;" },
            SalesSupport = new
            {
                EnvironmentCode = "DEVELOPMENT",
                Site = new { Name = "検証サイト", AuthenticationMode = mode.ToString() },
                Application = new { Name = "検証ポータル" },
                Portal = new { BaseUrl = "https://example.invalid/" },
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
        public string ApplicationName { get; set; } = typeof(SiteAuthenticationModeTests).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>DBを参照してはいけない検証でContextの作成を検出します。</summary>
    private sealed class RejectFactory : IDbContextFactory<CommonDbContext>
    {
        /// <summary>Contextの作成を失敗として扱います。</summary>
        public CommonDbContext CreateDbContext() => throw new InvalidOperationException("DBを参照しました。");
    }

    /// <summary>サイトのDB設定を取得してはいけない検証で参照を検出します。</summary>
    private sealed class RejectSettings : ISystemSettingsReader
    {
        /// <summary>サイト公開状態のDB取得を検出します。</summary>
        public Task<string> GetPublicationStatusAsync(CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>非公開案内のDB取得を検出します。</summary>
        public Task<string> GetPrivateMessageAsync(CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>業務日付のDB取得を検出します。</summary>
        public Task<DateOnly?> GetBusinessDateAsync(CancellationToken ct = default) => throw new InvalidOperationException();
    }
}
