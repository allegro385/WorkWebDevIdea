using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Data;

namespace SalesSupport.Common.FileStorage;

/// <summary>DBで管理するアップロード用途です。</summary>
public enum UploadPurpose { InquiryAttachment, UserImport, Reference, App, ToolInput }
/// <summary>要求内で確定したアップロード条件です。容量の単位はバイトです。</summary>
public sealed record UploadPolicySnapshot(int PolicyId, UploadPurpose Purpose, string? ToolId, long MaxFileSizeBytes, IReadOnlyList<string> Extensions);
/// <summary>認可済みの用途からDBの許可条件を取得します。</summary>
public interface IUploadPolicyProvider
{
    /// <summary>用途とツールの組合せを検証して条件を返します。</summary>
    Task<UploadPolicySnapshot> GetAsync(UploadPurpose purpose, string? toolId = null, CancellationToken ct = default);
}
/// <summary>ポリシーと拡張子を同じクエリで取得し、要求内で共有します。</summary>
public sealed class UploadPolicyProvider(IDbContextFactory<CommonDbContext> factory) : IUploadPolicyProvider
{
    private readonly Dictionary<(UploadPurpose, string?), UploadPolicySnapshot> cache = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>欠落・重複・不正値を許可へ変換しません。</summary>
    public async Task<UploadPolicySnapshot> GetAsync(UploadPurpose purpose, string? toolId = null, CancellationToken ct = default)
    {
        var (scope, code) = purpose switch
        {
            UploadPurpose.InquiryAttachment when toolId is null => ("SITE", "INQUIRY_ATTACHMENT"),
            UploadPurpose.UserImport when toolId is null => ("SITE", "USER_IMPORT"),
            UploadPurpose.Reference when !string.IsNullOrWhiteSpace(toolId) => ("TOOL_COMMON", "REFERENCE"),
            UploadPurpose.App when !string.IsNullOrWhiteSpace(toolId) => ("TOOL_COMMON", "APP"),
            UploadPurpose.ToolInput when !string.IsNullOrWhiteSpace(toolId) => ("TOOL", "TOOL_INPUT"),
            _ => throw new ArgumentException("アップロード用途が不正です。")
        };
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue((purpose, toolId), out var cached)) return cached;
            await using var db = await factory.CreateDbContextAsync(ct);
            if (toolId is not null)
            {
                var tool = await db.Tools.AsNoTracking().SingleOrDefaultAsync(x => x.ToolId == toolId, ct);
                if (tool is null || purpose == UploadPurpose.App && tool.ToolType != "DESKTOP" || purpose == UploadPurpose.ToolInput && tool.ToolType != "WEB")
                    throw new ArgumentException("対象ツールが不正です。");
            }
            var policyTool = scope == "TOOL" ? toolId : null;
            var rows = await (from policy in db.UploadPolicies.AsNoTracking()
                              join extension in db.UploadExtensions.AsNoTracking() on policy.PolicyId equals extension.PolicyId
                              where policy.ScopeType == scope && policy.PurposeCode == code && policy.ToolId == policyTool
                              select new { policy.PolicyId, policy.MaxFileSizeBytes, extension.Extension }).ToListAsync(ct);
            if (rows.Count == 0 || rows.Select(x => x.PolicyId).Distinct().Count() != 1 || rows.Any(x => x.MaxFileSizeBytes <= 0 || !UploadValidator.IsExtension(x.Extension)))
                throw new ConfigurationException("FileUploadPolicies");
            var extensions = Array.AsReadOnly(rows.Select(x => x.Extension).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
            var result = new UploadPolicySnapshot(rows[0].PolicyId, purpose, toolId, rows[0].MaxFileSizeBytes, extensions);
            cache.Add((purpose, toolId), result);
            return result;
        }
        finally { gate.Release(); }
    }
}
