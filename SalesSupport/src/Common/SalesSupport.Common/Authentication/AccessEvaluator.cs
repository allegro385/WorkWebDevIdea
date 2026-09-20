using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Data;

namespace SalesSupport.Common.Authentication;

/// <summary>利用制御を行う操作の種類です。</summary>
public enum AccessPurpose { Site, ToolDetail, ToolUse, ToolDownload, ToolManage }
/// <summary>認可対象の目的とツールです。</summary>
public sealed record AccessRequest(AccessPurpose Purpose, string? ToolId = null);
/// <summary>許可または拒否応答の判定です。</summary>
public sealed record AccessDecision(bool Allowed, int StatusCode = 200, string? FailureReason = null);
/// <summary>検証済みユーザーだけを参照します。</summary>
public interface ICurrentUserAccessor
{
    CurrentUser? User { get; }
}
/// <summary>要求スコープ内の検証済み情報を保持します。</summary>
public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    public CurrentUser? User { get; internal set; }
}
/// <summary>サイトとツールの現行状態で利用を判定します。</summary>
public interface IAccessEvaluator
{
    /// <summary>認証・サイト・ツール状態を確認します。</summary>
    Task<AccessDecision> EvaluateAsync(AccessRequest request, CancellationToken ct = default);
}
/// <summary>表示可能性と実行権限を混同せずに判定します。</summary>
public sealed class AccessEvaluator(ICurrentUserAccessor current, ISystemSettingsReader settings, IDbContextFactory<CommonDbContext> factory) : IAccessEvaluator
{
    /// <summary>未定義状態や取得障害は例外とし、許可へ変換しません。</summary>
    public async Task<AccessDecision> EvaluateAsync(AccessRequest request, CancellationToken ct = default)
    {
        var user = current.User;
        if (user is null) return new(false, 401, "UNAUTHENTICATED");
        if (user.RoleCode is not ("USER" or "ADMIN")) return new(false, 403, "ROLE_DENIED");
        var publication = await settings.GetPublicationStatusAsync(ct);
        if (publication == "PRIVATE" && user.RoleCode != "ADMIN") return new(false, 403, "SITE_PRIVATE");
        if (request.Purpose == AccessPurpose.Site) return new(true);
        if (!Enum.IsDefined(request.Purpose) || string.IsNullOrWhiteSpace(request.ToolId)) return new(false, 400, "INVALID_INPUT");
        await using var db = await factory.CreateDbContextAsync(ct);
        var tool = await db.Tools.AsNoTracking().SingleOrDefaultAsync(x => x.ToolId == request.ToolId, ct);
        if (tool is null) return new(false, 404, "TOOL_UNAVAILABLE");
        if (tool.Status is not ("PUBLIC" or "PRIVATE" or "HIDDEN") || tool.ToolType is not ("WEB" or "DESKTOP" or "DOCUMENT"))
            throw new ConfigurationException("Tools/StatusOrType");
        if (request.Purpose == AccessPurpose.ToolManage) return user.RoleCode == "ADMIN" ? new(true) : new(false, 403, "ROLE_DENIED");
        if (tool.Status == "HIDDEN") return new(false, 404, "TOOL_UNAVAILABLE");
        if (tool.Status == "PRIVATE" && user.RoleCode != "ADMIN") return new(false, 403, "ROLE_DENIED");
        if (request.Purpose == AccessPurpose.ToolUse && tool.ToolType != "WEB") return new(false, 404, "TOOL_UNAVAILABLE");
        return new(true);
    }
}
