namespace SalesSupport.Common.Entities.Configuration;

/// <summary>DB運用で管理する共通設定です。</summary>
public sealed class SystemSetting : AuditedEntity
{
    public string SettingCategory { get; set; } = "";
    public string SettingKey { get; set; } = "";
    public string SettingName { get; set; } = "";
    public string? SettingValue { get; set; }
    public string? Description { get; set; }
}
