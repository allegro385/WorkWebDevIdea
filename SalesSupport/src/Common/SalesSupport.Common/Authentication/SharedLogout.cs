using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Logging;

namespace SalesSupport.Common.Authentication;

/// <summary>表示中アプリで共有Cookieを破棄し、Portalへ戻すログアウト処理です。</summary>
public static class SharedLogoutExtensions
{
    /// <summary>認証必須のPOSTとして登録します。サイト公開状態・ツール状態は条件にしません。</summary>
    public static IEndpointConventionBuilder MapSalesSupportLogout(this IEndpointRouteBuilder endpoints, string pattern = "/account/logout") =>
        endpoints.MapPost(pattern, HandleAsync)
            .WithMetadata(new NoSlidingRenewalAttribute())
            .RequireAuthorization(new AuthorizationPolicyBuilder(SharedCookieContract.Scheme).RequireAuthenticatedUser().Build());

    /// <summary>自アプリのCSRFトークンを検証してから共有Cookieを削除します。</summary>
    private static async Task<IResult> HandleAsync(HttpContext context, IAntiforgery antiforgery, IActivityLogger logger, IOptions<CommonOptions> options)
    {
        try { await antiforgery.ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { return Results.StatusCode(StatusCodes.Status400BadRequest); }
        await context.SignOutAsync(SharedCookieContract.Scheme);
        await logger.WriteAsync(new ActivityEvent("LOGOUT", "SUCCESS"), context.RequestAborted);
        return Results.Redirect(options.Value.PortalBaseUrl);
    }
}
