using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.Authentication;

/// <summary>サイト入場と必要に応じた管理者・ツール利用の要件です。</summary>
public sealed record CommonAccessRequirement(bool Admin = false, bool Tool = false) : IAuthorizationRequirement;

/// <summary>共通利用制御をASP.NET Core認可へ接続します。</summary>
public sealed class CommonAuthorizationHandler(IAccessEvaluator evaluator, ICurrentUserAccessor current, IOptions<CommonOptions> options,
    IHttpContextAccessor http) : AuthorizationHandler<CommonAccessRequirement>
{
    internal const string DecisionKey = "SalesSupport.AccessDecision";

    /// <summary>許可時だけ要件を満たし、障害も拒否側へ倒します。</summary>
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, CommonAccessRequirement requirement)
    {
        AccessDecision decision;
        try
        {
            var request = requirement.Tool ? new AccessRequest(AccessPurpose.ToolUse, options.Value.ToolId) : new AccessRequest(AccessPurpose.Site);
            decision = await evaluator.EvaluateAsync(request, http.HttpContext?.RequestAborted ?? default);
            if (decision.Allowed && requirement.Admin && current.User?.RoleCode != "ADMIN") decision = new(false, 403, "ROLE_DENIED");
        }
        catch (OperationCanceledException) when (http.HttpContext?.RequestAborted.IsCancellationRequested == true) { throw; }
        catch (Exception) { decision = new(false, 503, "DEPENDENCY_UNAVAILABLE"); }
        if (decision.Allowed) context.Succeed(requirement);
        else
        {
            if (http.HttpContext is { } requestContext) requestContext.Items[DecisionKey] = decision;
            context.Fail();
        }
    }
}

/// <summary>認可結果のAPI応答と非公開案内を分離します。</summary>
public sealed class CommonAuthorizationResultHandler(IOptions<CommonOptions> options) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    /// <summary>DB障害を認証不足に変換せず503として返します。</summary>
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Succeeded) { await next(context); return; }
        if (context.Items.TryGetValue(CommonAuthorizationHandler.DecisionKey, out var value) && value is AccessDecision decision)
        {
            if (decision.FailureReason == "SITE_PRIVATE" && !context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.Redirect(new Uri(new Uri(options.Value.PortalBaseUrl), "private").AbsoluteUri);
                return;
            }
            if (decision.StatusCode != 401) { context.Response.StatusCode = decision.StatusCode; return; }
        }
        await fallback.HandleAsync(next, context, policy, result);
    }
}
