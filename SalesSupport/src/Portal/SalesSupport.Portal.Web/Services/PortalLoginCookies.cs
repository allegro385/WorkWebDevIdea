using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Identity;

namespace SalesSupport.Portal.Web.Services;

/// <summary>パスワード確認後の一時Cookieと、本人が選択したブラウザー記憶を管理します。</summary>
public sealed class PortalLoginCookies(IHttpContextAccessor accessor, IApplicationClock clock)
{
    public const string PendingScheme = "SalesSupport.OtpPending";
    public const string RememberScheme = "SalesSupport.OtpRemember";

    /// <summary>OTP関係のCookieだけをPortalへ登録し、通常の認証スキームから分離します。</summary>
    public static void Register(IServiceCollection services)
    {
        services.AddAuthentication().AddCookie(PendingScheme, options => Configure(options, ".SalesSupport.OtpPending", TimeSpan.FromMinutes(15)))
            .AddCookie(RememberScheme, options => Configure(options, ".SalesSupport.OtpRemember", TimeSpan.FromDays(30)));
    }

    /// <summary>更新による期限延長を無効にし、HTTPSでだけ送信します。</summary>
    private static void Configure(CookieAuthenticationOptions options, string name, TimeSpan lifetime)
    {
        options.Cookie.Name = name;
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = lifetime;
        options.SlidingExpiration = false;
    }

    /// <summary>一時Cookieを読み、OTP画面の要求をパスワード確認済みの試行へ束縛します。</summary>
    public async Task<PendingLogin?> GetPendingAsync()
    {
        var result = await Context.AuthenticateAsync(PendingScheme);
        if (!result.Succeeded || !Guid.TryParse(result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(result.Principal?.FindFirstValue("attempt"), out var attemptId)
            || result.Properties?.ExpiresUtc is not { } until || until <= clock.GetUtcNow()) return null;
        result.Properties.Items.TryGetValue("returnUrl", out var returnUrl);
        return new(userId, attemptId, returnUrl);
    }

    /// <summary>パスワード確認後に一時Cookieを発行し、共有ログインCookieはまだ発行しません。</summary>
    public Task SetPendingAsync(Guid userId, Guid attemptId, string? returnUrl) => Context.SignInAsync(PendingScheme,
        Principal(PendingScheme, new(ClaimTypes.NameIdentifier, userId.ToString()), new("attempt", attemptId.ToString())),
        new AuthenticationProperties { IsPersistent = false, ExpiresUtc = clock.GetUtcNow().AddMinutes(15), Items = { ["returnUrl"] = returnUrl } });

    /// <summary>資格情報の変更も照合し、保存済みの記憶だけでログインを完了させません。</summary>
    public async Task<bool> IsRememberedAsync(ApplicationUser user)
    {
        var result = await Context.AuthenticateAsync(RememberScheme);
        return result.Succeeded && result.Properties?.ExpiresUtc > clock.GetUtcNow()
            && result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id.ToString()
            && result.Principal.FindFirstValue("stamp") == user.SecurityStamp
            && result.Principal.FindFirstValue("login") == user.UserName
            && result.Principal.FindFirstValue("email") == user.Email;
    }

    /// <summary>OTP成功後にだけ、本人が選択した30日間の固定期限を保存します。</summary>
    public Task RememberAsync(ApplicationUser user) => Context.SignInAsync(RememberScheme,
        Principal(RememberScheme, new(ClaimTypes.NameIdentifier, user.Id.ToString()), new("stamp", user.SecurityStamp!),
            new("login", user.UserName!), new("email", user.Email!)),
        new AuthenticationProperties { IsPersistent = true, ExpiresUtc = clock.GetUtcNow().AddDays(30), AllowRefresh = false });

    /// <summary>ログイン完了または再開始時に一時Cookieだけを破棄します。</summary>
    public Task ClearPendingAsync() => Context.SignOutAsync(PendingScheme);

    /// <summary>指定用途のCookieでだけ使用する本人情報を構成します。</summary>
    private static ClaimsPrincipal Principal(string scheme, params Claim[] claims) => new(new ClaimsIdentity(claims, scheme));

    private HttpContext Context => accessor.HttpContext ?? throw new InvalidOperationException("認証要求のコンテキストがありません。");
}

/// <summary>保護された一時Cookieから取得した、パスワード確認済みの試行です。</summary>
public sealed record PendingLogin(Guid UserId, Guid AttemptId, string? ReturnUrl);
