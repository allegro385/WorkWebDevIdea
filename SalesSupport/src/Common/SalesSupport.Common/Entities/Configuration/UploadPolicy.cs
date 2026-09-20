namespace SalesSupport.Common.Entities.Configuration;

/// <summary>用途ごとの容量制限です。</summary>
public sealed class UploadPolicy : AuditedEntity
{
    public int PolicyId { get; set; }
    public string ScopeType { get; set; } = "";
    public string? ToolId { get; set; }
    public string PurposeCode { get; set; } = "";
    public long MaxFileSizeBytes { get; set; }
}
