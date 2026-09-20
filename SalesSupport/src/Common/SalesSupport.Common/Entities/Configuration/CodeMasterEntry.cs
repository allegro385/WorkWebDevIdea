namespace SalesSupport.Common.Entities.Configuration;

/// <summary>コードの表示情報を保持します。</summary>
public sealed class CodeMasterEntry : AuditedEntity
{
    public string CodeType { get; set; } = "";
    public string CodeValue { get; set; } = "";
    public string CodeName { get; set; } = "";
    public int SortOrder { get; set; }
    public string? ColorCode { get; set; }
}
