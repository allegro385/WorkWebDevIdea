using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Data;
using SalesSupport.Common.DateTime;

namespace SalesSupport.Common.Authentication;

/// <summary>Cookie更新の対象外であることを明示します。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class NoSlidingRenewalAttribute : Attribute { }
/// <summary>利用開始ログの対象となる入口画面を示します。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolEntryAttribute : Attribute { }
/// <summary>Portalとツールが共有するCookie契約です。</summary>
public static class SharedCookieContract
{
    public const string Scheme = "Identity.Application";
    public const string CookieName = ".SalesSupport.Auth";
    public const string InitialUtc = "sales_support.initial_utc";

    /// <summary>ログイン成功時だけ初回時刻を設定します。</summary>
    public static AuthenticationProperties CreateSignInProperties(DateTimeOffset utc) => new()
    {
        IsPersistent = false,
        Items = { [InitialUtc] = utc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) }
    };
}

/// <summary>毎要求のユーザー再検証と最大認証期間を適用します。</summary>
public sealed class SharedCookieEvents(IDbContextFactory<CommonDbContext> factory, CurrentUserAccessor current, IApplicationClock clock,
    IOptions<CommonOptions> options, IOptions<IdentityOptions> identityOptions) : CookieAuthenticationEvents
{
    /// <summary>通常のスライド判定を維持し、指定要求だけ更新を抑止します。</summary>
    public override Task CheckSlidingExpiration(CookieSlidingExpirationContext context)
    {
        if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<NoSlidingRenewalAttribute>() is not null) context.ShouldRenew = false;
        return Task.CompletedTask;
    }

    /// <summary>初回時刻・ユーザー状態・SecurityStampをDBと照合します。</summary>
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        current.User = null;
        var claims = identityOptions.Value.ClaimsIdentity;
        var now = clock.GetUtcNow();
        var valid = Guid.TryParse(context.Principal?.FindFirstValue(claims.UserIdClaimType), out var userId)
            && context.Properties.Items.TryGetValue(SharedCookieContract.InitialUtc, out var text)
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var initial)
            && initial <= now.ToUnixTimeSeconds() && initial > now.ToUnixTimeSeconds() - (long)TimeSpan.FromHours(8).TotalSeconds;
        if (!valid) { await RejectAsync(context); return; }
        // 取得障害は例外としてホストへ伝え、Cookieを消して不存在扱いにはしない。
        await using var db = await factory.CreateDbContextAsync(context.HttpContext.RequestAborted);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, context.HttpContext.RequestAborted);
        var stamp = context.Principal?.FindFirstValue(claims.SecurityStampClaimType);
        if (user is null || !user.IsActive || user.RoleCode is not ("USER" or "ADMIN") || string.IsNullOrEmpty(stamp) || stamp != user.SecurityStamp)
        {
            await RejectAsync(context);
            return;
        }
        current.User = new CurrentUser(user.UserId, user.DisplayName, user.RoleCode);
    }

    /// <summary>APIは401、HTMLはPortalのログイン画面へ誘導します。</summary>
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else
        {
            var login = new Uri(new Uri(options.Value.PortalBaseUrl), "account/login");
            var returnUrl = context.Request.PathBase.Add(context.Request.Path).Value ?? "/";
            context.Response.Redirect(login + "?returnUrl=" + Uri.EscapeDataString(returnUrl));
        }
        return Task.CompletedTask;
    }

    /// <summary>権限不足はログインへ戻さず403を返します。</summary>
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    /// <summary>検証に失敗した共有Cookieを破棄します。</summary>
    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(SharedCookieContract.Scheme);
    }
}
