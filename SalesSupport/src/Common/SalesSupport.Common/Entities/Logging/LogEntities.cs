namespace SalesSupport.Common.Entities.Logging;

/// <summary>ツールの起動・実行・取得を記録します。</summary>
public sealed class ToolUsageLog
{
    public long ToolUsageLogId { get; set; }
    public string ToolId { get; set; } = "";
    public Guid UserId { get; set; }
    public System.DateTime OccurredAt { get; set; }
    public string EventType { get; set; } = "";
    public string ResultCode { get; set; } = "";
    public Guid? CorrelationId { get; set; }
}
/// <summary>業務操作の種別と結果を記録します。</summary>
public sealed class UserActivityLog
{
    public long UserActivityLogId { get; set; }
    public Guid? UserId { get; set; }
    public System.DateTime OccurredAt { get; set; }
    public string EventType { get; set; } = "";
    public string ResultCode { get; set; } = "";
    public string? FailureReason { get; set; }
    public string? RequestPath { get; set; }
    public string? OperationTargetType { get; set; }
    public string? OperationTargetId { get; set; }
    public string? OperationDetails { get; set; }
    public string? IpAddress { get; set; }
    public Guid? CorrelationId { get; set; }
}
/// <summary>安全化済みの障害情報を追跡番号に紐付けます。</summary>
public sealed class SystemErrorLog
{
    public long SystemErrorLogId { get; set; }
    public Guid ErrorId { get; set; }
    public string? ErrorCode { get; set; }
    public System.DateTime OccurredAt { get; set; }
    public string ApplicationName { get; set; } = "";
    public string? ToolId { get; set; }
    public Guid? UserId { get; set; }
    public string ErrorLevel { get; set; } = "ERROR";
    public string? ErrorType { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string? StackTrace { get; set; }
    public string? RequestPath { get; set; }
    public string? HttpMethod { get; set; }
    public Guid? CorrelationId { get; set; }
}
