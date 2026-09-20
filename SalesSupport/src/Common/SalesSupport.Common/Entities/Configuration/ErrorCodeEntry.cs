namespace SalesSupport.Common.Entities.Configuration;

/// <summary>安全なエラー表示文言です。</summary>
public sealed class ErrorCodeEntry : AuditedEntity
{
    public string ToolId { get; set; } = "";
    public int ErrorNo { get; set; }
    public string ErrorCode { get; set; } = "";
    public string DisplayMessage { get; set; } = "";
    public string ErrorLevel { get; set; } = "";
}
