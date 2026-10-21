using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Logging;

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
            // 単独開発は認証なしで利用させるため、自端末以外からの要求はツールが存在しない扱いで拒否します。
            decision = options.Value.IsStandalone && !IsLoopback(http.HttpContext)
                ? new(false, 404, "TOOL_UNAVAILABLE")
                : await evaluator.EvaluateAsync(request, http.HttpContext?.RequestAborted ?? default);
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

    /// <summary>接続元が自端末かを判定します。接続元が不明な要求は自端末として扱いません。</summary>
    /// <param name="context">判定する要求です。</param>
    /// <returns>ループバックアドレスからの要求ならtrueです。</returns>
    private static bool IsLoopback(HttpContext? context) => context?.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address);
}

/// <summary>認可結果のAPI応答と非公開案内を分離し、拒否を操作ログへ残します。</summary>
public sealed class CommonAuthorizationResultHandler(IOptions<CommonOptions> options, IActivityLogger activity) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    /// <summary>DB障害を認証不足に変換せず503として返します。</summary>
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Succeeded) { await next(context); return; }
        if (context.Items.TryGetValue(CommonAuthorizationHandler.DecisionKey, out var value) && value is AccessDecision decision)
        {
            await RecordAsync(context, decision);
            if (decision.FailureReason == "SITE_PRIVATE" && !context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.Redirect(new Uri(new Uri(options.Value.PortalBaseUrl), "private").AbsoluteUri);
                return;
            }
            if (decision.StatusCode != 401) { context.Response.StatusCode = decision.StatusCode; return; }
        }
        await fallback.HandleAsync(next, context, policy, result);
    }

    /// <summary>利用者を特定できた拒否だけを記録します。未認証のログイン誘導は通常の導線のため残しません。</summary>
    /// <param name="context">拒否した要求です。経路とIPはログ側が記録します。</param>
    /// <param name="decision">拒否理由を保持する判定結果です。</param>
    private async Task RecordAsync(HttpContext context, AccessDecision decision)
    {
        if (decision.FailureReason is null or "UNAUTHENTICATED") return;
        await activity.WriteAsync(new ActivityEvent("ACCESS_DENIED", "DENIED", decision.FailureReason), context.RequestAborted);
    }
}
