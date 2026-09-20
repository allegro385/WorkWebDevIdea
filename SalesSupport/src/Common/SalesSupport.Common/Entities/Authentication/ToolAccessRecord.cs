namespace SalesSupport.Common.Entities.Authentication;

/// <summary>要求ごとの利用制御に必要なツール列です。</summary>
public sealed class ToolAccessRecord
{
    public string ToolId { get; set; } = "";
    public string ToolName { get; set; } = "";
    public string ToolType { get; set; } = "";
    public string Status { get; set; } = "";
}
