namespace SalesSupport.Common.Entities;

/// <summary>DBが設定する監査列を共有します。</summary>
public abstract class AuditedEntity
{
    public int UpdateCount { get; set; }
    public System.DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public System.DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}
 
