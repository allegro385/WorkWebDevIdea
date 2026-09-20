namespace SalesSupport.Common.Entities.Configuration;

/// <summary>用途ごとの許可拡張子です。</summary>
public sealed class UploadPolicyExtension : AuditedEntity
{
    public int PolicyId { get; set; }
    public string Extension { get; set; } = "";
}
