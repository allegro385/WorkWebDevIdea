using System.Diagnostics;
using System.Transactions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Data;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Logging;

namespace SalesSupport.Common.Logging;

/// <summary>ログ保存の成否です。本処理の成否とは独立しています。</summary>
public enum LogWriteResult { Written, Failed, Skipped }
/// <summary>ログ処理の独立した待機上限を秒で指定します。</summary>
public sealed class LoggingOptions { public int TimeoutSeconds { get; set; } = 3; }
/// <summary>外部から受け取らない要求内相関IDを保持します。</summary>
public sealed class RequestCorrelation { public Guid Id { get; } = Guid.NewGuid(); }
/// <summary>認可済みのツール利用結果です。</summary>
public sealed record UsageEvent(string ToolId, string EventType, string ResultCode);
/// <summary>コード値だけで表した変更前後です。表示名・入力本文は保持しません。</summary>
public sealed record ActivityChange(string Field, string? FromCode, string? ToCode);
/// <summary>本文やメールを含めず操作種別と結果だけを伝えます。</summary>
public sealed record ActivityEvent(string EventType, string ResultCode, string? FailureReason = null,
    string? TargetType = null, string? TargetId = null, IReadOnlyList<ActivityChange>? Changes = null);
/// <summary>例外のメッセージは保存せず型・コードと許可した補足情報だけを扱います。</summary>
public sealed record SystemErrorEvent(Guid ErrorId, string? ErrorCode = null, Exception? Exception = null, string ErrorLevel = "ERROR",
    IReadOnlyDictionary<string, string>? Details = null);
/// <summary>ツール利用ログの境界です。</summary>
public interface IUsageLogger
{
    /// <summary>検証済みユーザーの利用を一度記録します。</summary>
    Task<LogWriteResult> WriteAsync(UsageEvent entry, CancellationToken ct = default);
}
/// <summary>ユーザー操作ログの境界です。</summary>
public interface IActivityLogger
{
    /// <summary>秘密値を含まない操作結果を一度記録します。</summary>
    Task<LogWriteResult> WriteAsync(ActivityEvent entry, CancellationToken ct = default);
}
/// <summary>障害ログの境界です。</summary>
public interface ISystemErrorLogger
{
    /// <summary>記録失敗時に再帰ログや再試行を行いません。</summary>
    Task<LogWriteResult> WriteAsync(SystemErrorEvent entry, CancellationToken ct = default);
}
/// <summary>独立Contextと時間制限で3種類のログを記録します。</summary>
public sealed class CommonLogger(IDbContextFactory<LogDbContext> factory, ICurrentUserAccessor current, IApplicationClock clock,
    RequestCorrelation correlation, IOptions<CommonOptions> application, IOptions<LoggingOptions> settings,
    IHttpContextAccessor http) : IUsageLogger, IActivityLogger, ISystemErrorLogger
{
    private static readonly HashSet<string> ActivityTypes = new("LOGIN LOGOUT ACCOUNT_LOCK ACCESS_DENIED PASSWORD_SETUP PASSWORD_RESET PASSWORD_UPDATE PASSWORD_RESET_REQUEST USER_CREATE USER_UPDATE USER_UNLOCK TOOL_UPDATE TOOL_ORDER_UPDATE NOTICE_CREATE NOTICE_UPDATE VERSION_CREATE VERSION_UPDATE VERSION_DELETE VERSION_SELECT FILE_CREATE FILE_REPLACE FILE_UPDATE FILE_DELETE FILE_ORDER_UPDATE INQUIRY_CREATE INQUIRY_UPDATE INQUIRY_CLASSIFICATION_UPDATE PREFERENCE_UPDATE FAVORITE_ADD FAVORITE_REMOVE MAIL_SEND".Split(' '), StringComparer.Ordinal);
    private static readonly HashSet<string> FailureReasons = new("INVALID_CREDENTIALS ACCOUNT_LOCKED UNAUTHENTICATED INACTIVE_USER STAMP_MISMATCH ROLE_DENIED SITE_PRIVATE TOOL_UNAVAILABLE INVALID_INPUT CONFLICT DEPENDENCY_UNAVAILABLE SEND_FAILED SEND_UNKNOWN NO_RECIPIENTS CANCELLED".Split(' '), StringComparer.Ordinal);

    /// <summary>未認証の利用記録を拒否し、ツールホストでは設定IDを使います。</summary>
    public Task<LogWriteResult> WriteAsync(UsageEvent entry, CancellationToken ct = default)
    {
        var toolId = application.Value.Kind == Contracts.ApplicationKind.Tool ? application.Value.ToolId : entry.ToolId;
        if (current.User is null || !IsCode(toolId, 20) || entry.EventType is not ("DESKTOP_DOWNLOAD" or "WEB_OPEN" or "WEB_EXECUTE") || entry.ResultCode is not ("SUCCESS" or "FAILURE"))
            return Task.FromResult(LogWriteResult.Skipped);
        return SaveAsync(new ToolUsageLog { ToolId = toolId!, UserId = current.User.UserId, EventType = entry.EventType, ResultCode = entry.ResultCode, OccurredAt = clock.GetUtcNow().UtcDateTime, CorrelationId = correlation.Id });
    }

    /// <summary>固定コードだけを記録し、入力データを受け付けません。</summary>
    public Task<LogWriteResult> WriteAsync(ActivityEvent entry, CancellationToken ct = default)
    {
        if (!ActivityTypes.Contains(entry.EventType) || entry.ResultCode is not ("SUCCESS" or "FAILURE" or "DENIED") || entry.FailureReason is not null && !FailureReasons.Contains(entry.FailureReason))
            return Task.FromResult(LogWriteResult.Skipped);
        var request = http.HttpContext?.Request;
        return SaveAsync(new UserActivityLog
        {
            UserId = current.User?.UserId, EventType = entry.EventType, ResultCode = entry.ResultCode, FailureReason = entry.FailureReason,
            OperationTargetType = IsCode(entry.TargetType, 30) ? entry.TargetType : null,
            OperationTargetId = IsCode(entry.TargetId, 100) ? entry.TargetId : null,
            OperationDetails = Changes(entry.Changes), RequestPath = MaskPath(request), IpAddress = RemoteAddress(),
            OccurredAt = clock.GetUtcNow().UtcDateTime, CorrelationId = correlation.Id
        });
    }

    /// <summary>例外Message・Data・物理パスを読まず、型とメソッド名を抽出します。</summary>
    public Task<LogWriteResult> WriteAsync(SystemErrorEvent entry, CancellationToken ct = default)
    {
        if (entry.ErrorId == Guid.Empty || entry.ErrorLevel is not ("ERROR" or "CRITICAL")) return Task.FromResult(LogWriteResult.Skipped);
        var request = http.HttpContext?.Request;
        var message = "処理中にエラーが発生しました。" + SafeLogDetails.Format(entry.Details);
        return SaveAsync(new SystemErrorLog
        {
            ErrorId = entry.ErrorId, ErrorCode = IsCode(entry.ErrorCode, 100) ? entry.ErrorCode : null,
            OccurredAt = clock.GetUtcNow().UtcDateTime, ApplicationName = application.Value.ApplicationName,
            ToolId = application.Value.ToolId, UserId = current.User?.UserId, ErrorLevel = entry.ErrorLevel,
            ErrorType = entry.Exception?.GetType().FullName is { } type ? type[..Math.Min(type.Length, 300)] : null,
            ErrorMessage = message[..Math.Min(message.Length, 2000)], StackTrace = SafeStack(entry.Exception),
            RequestPath = MaskPath(request), HttpMethod = IsCode(request?.Method, 10) ? request!.Method : null, CorrelationId = correlation.Id
        });
    }

    /// <summary>変更内容をコード値だけで連結し、コード以外の値を捨てます。</summary>
    private static string? Changes(IReadOnlyList<ActivityChange>? changes)
    {
        if (changes is null || changes.Count == 0) return null;
        var text = string.Join(';', changes
            .Where(x => AllowedChange(x.Field, x.FromCode) && AllowedChange(x.Field, x.ToCode))
            .Select(x => $"{x.Field}:{x.FromCode ?? "-"}>{x.ToCode ?? "-"}"));
        return text.Length == 0 ? null : text[..Math.Min(text.Length, 1000)];
    }

    /// <summary>業務で許可したフィールドと保存コードの組合せだけを記録します。</summary>
    private static bool AllowedChange(string field, string? code) => field switch
    {
        "RoleCode" => code is null or "USER" or "ADMIN",
        "Status" => code is null or "PUBLIC" or "PRIVATE" or "HIDDEN" or "ACTION_REQUIRED" or "IN_PROGRESS" or "UNDER_REVIEW" or "COMPLETED" or "NO_ACTION",
        "IsActive" or "SystemNoticeMailEnabled" or "FavoriteToolNoticeMailEnabled" => code is null or "0" or "1",
        _ => false
    };

    /// <summary>要求値ではなくサーバー定義のルートテンプレートを記録し、トークンの漏えいを防ぎます。</summary>
    private static string? MaskPath(Microsoft.AspNetCore.Http.HttpRequest? request)
    {
        if (request is null) return null;
        if (request.HttpContext.GetEndpoint() is not Microsoft.AspNetCore.Routing.RouteEndpoint endpoint) return null;
        var text = endpoint.RoutePattern.RawText ?? "";
        return text.Length == 0 ? null : text[..Math.Min(text.Length, 500)];
    }

    /// <summary>信頼済みプロキシ適用後の接続元IPを取得します。</summary>
    private string? RemoteAddress()
    {
        var address = http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        return address is { Length: > 0 and <= 45 } ? address : null;
    }

    /// <summary>SQL待機と接続を独立した上限内に制限し、業務トランザクションから分離します。</summary>
    private async Task<LogWriteResult> SaveAsync(object entity)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.Value.TimeoutSeconds));
            using var scope = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
            await using var db = await factory.CreateDbContextAsync(timeout.Token);
            db.Add(entity);
            await db.SaveChangesAsync(timeout.Token);
            scope.Complete();
            return LogWriteResult.Written;
        }
        catch (Exception) { return LogWriteResult.Failed; }
    }

    /// <summary>ログ識別コードに限定された文字だけを許可します。</summary>
    private static bool IsCode(string? value, int max) => value is { Length: > 0 } && value.Length <= max && value.All(x => char.IsAsciiLetterOrDigit(x) || x is '_' or '-');

    /// <summary>ファイル名・行番号・引数値のないスタックを最大30フレーム抽出します。</summary>
    private static string? SafeStack(Exception? exception) => exception is null ? null : string.Join('\n', new StackTrace(exception, false).GetFrames().Take(30)
        .Select(x => x.GetMethod()).Where(x => x is not null).Select(x => $"{x!.DeclaringType?.FullName}.{x.Name}"));
}
