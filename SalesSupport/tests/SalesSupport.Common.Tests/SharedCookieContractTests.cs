using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Data;
using SalesSupport.Common.DateTime;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>OTP完了前のCookie拒否と、ツールからPortalへの戻り先契約を検証します。</summary>
public sealed class SharedCookieContractTests
{
    /// <summary>旧方式または未知の認証方針を持つCookieをDB照会前に拒否します。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("password_only")]
    public async Task OldCookieCannotAccessProtectedResources(string? policy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddAuthentication(SharedCookieContract.Scheme).AddCookie(SharedCookieContract.Scheme);
        await using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        var properties = SharedCookieContract.CreateSignInProperties(DateTimeOffset.UtcNow);
        properties.Items[SharedCookieContract.LoginPolicy] = policy;
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], SharedCookieContract.Scheme));
        var validation = new CookieValidatePrincipalContext(context, Scheme(), new(), new AuthenticationTicket(principal, properties, SharedCookieContract.Scheme));
        await Events().ValidatePrincipal(validation);
        Assert.Null(validation.Principal);
        Assert.NotEmpty(context.Response.Headers.SetCookie);
    }

    /// <summary>ツールのPathBaseと検索条件を保持してPortalへリダイレクトします。</summary>
    [Fact]
    public async Task DirectToolRequestKeepsPathAndQuery()
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = "/sample";
        context.Request.Path = "/input";
        context.Request.QueryString = new QueryString("?item=1&mode=edit");
        await Events().RedirectToLogin(new RedirectContext<CookieAuthenticationOptions>(context, Scheme(), new(), new(), "/unused"));
        Assert.Equal("https://portal.test/account/login?returnUrl=%2Fsample%2Finput%3Fitem%3D1%26mode%3Dedit", context.Response.Headers.Location);
    }

    /// <summary>API要求へログイン画面を返さず401を返します。</summary>
    [Fact]
    public async Task ApiRequestDoesNotRedirectToLogin()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/data";
        await Events().RedirectToLogin(new RedirectContext<CookieAuthenticationOptions>(context, Scheme(), new(), new(), "/unused"));
        Assert.Equal(401, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    /// <summary>共有Cookieのスキーム情報を標準ハンドラーで作成します。</summary>
    private static AuthenticationScheme Scheme() => new(SharedCookieContract.Scheme, null, typeof(CookieAuthenticationHandler));

    /// <summary>DBに到達しない拒否・リダイレクト条件だけを確認します。</summary>
    private static SharedCookieEvents Events() => new(new RejectFactory(), new CurrentUserAccessor(), new ApplicationClock(TimeProvider.System),
        Options.Create(new CommonOptions { PortalBaseUrl = "https://portal.test/" }), Options.Create(new IdentityOptions()));

    /// <summary>旧Cookieの検証でDBに到達した場合に試験を失敗させます。</summary>
    private sealed class RejectFactory : IDbContextFactory<CommonDbContext>
    {
        /// <summary>不正Cookieの検証からDB接続を要求しません。</summary>
        public CommonDbContext CreateDbContext() => throw new InvalidOperationException("不正CookieでDBへ到達しました。");
    }
}
