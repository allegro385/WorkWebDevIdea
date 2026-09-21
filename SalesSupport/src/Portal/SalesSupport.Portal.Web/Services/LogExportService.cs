using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Data;
using SalesSupport.Common.DataExport;
using SalesSupport.Common.DateTime;
using SalesSupport.Portal.Web.Data;

namespace SalesSupport.Portal.Web.Services;

/// <summary>出力対象のログ種別です。</summary>
public enum LogKind
{
    /// <summary>ツール利用ログです。</summary>
    ToolUsage,
    /// <summary>ユーザー操作ログです。UIの呼称に対する物理名はlog.UserActivityLogsです。</summary>
    UserActivity,
    /// <summary>システムエラーログです。</summary>
    SystemError
}

/// <summary>抽出内容です。ツール利用ログだけ集計を選択できます。</summary>
public enum LogExtraction
{
    /// <summary>選択したログの全列・全件を出力します。</summary>
    Detail,
    /// <summary>ツール別の利用者数を出力します。</summary>
    ToolUserCount
}

/// <summary>A003 ログ管理のTSV出力を扱います。明細取得・集計・整形を分けて追加できる構成にします。</summary>
public interface ILogExportService
{
    /// <summary>選択に対応する出力列を、DB設計のログ列定義順で返します。</summary>
    ExportDefinition GetDefinition(LogKind kind, LogExtraction extraction);

    /// <summary>全件をメモリーへ読み込まず、主キー昇順で1件ずつ返します。</summary>
    IAsyncEnumerable<ExportRow> ReadAsync(LogKind kind, LogExtraction extraction, CancellationToken ct = default);

    /// <summary>種別と出力日時を含むファイル名を返します。</summary>
    string BuildFileName(LogKind kind, LogExtraction extraction);
}

/// <summary>固定の列リストで出力し、リフレクション順やSELECT *に依存しません。</summary>
public sealed class LogExportService(IDbContextFactory<LogDbContext> logs, PortalDbContext portal, IApplicationClock clock) : ILogExportService
{
    private static readonly string[] ToolUsageColumns =
        ["ToolUsageLogId", "ToolId", "UserId", "OccurredAt", "EventType", "ResultCode", "CorrelationId"];
    private static readonly string[] UserActivityColumns =
        ["UserActivityLogId", "UserId", "OccurredAt", "EventType", "ResultCode", "FailureReason", "RequestPath",
         "OperationTargetType", "OperationTargetId", "OperationDetails", "IpAddress", "CorrelationId"];
    private static readonly string[] SystemErrorColumns =
        ["SystemErrorLogId", "ErrorId", "ErrorCode", "OccurredAt", "ApplicationName", "ToolId", "UserId", "ErrorLevel",
         "ErrorType", "ErrorMessage", "StackTrace", "RequestPath", "HttpMethod", "CorrelationId"];
    private static readonly string[] ToolUserCountColumns = ["ToolName", "UserCount"];

    /// <summary>0件でも見出しを出力できるよう、選択だけで列を決定します。</summary>
    public ExportDefinition GetDefinition(LogKind kind, LogExtraction extraction)
    {
        if (kind == LogKind.ToolUsage && extraction == LogExtraction.ToolUserCount) return new(DelimitedFormat.Tsv, ToolUserCountColumns);
        return new(DelimitedFormat.Tsv, kind switch
        {
            LogKind.ToolUsage => ToolUsageColumns,
            LogKind.UserActivity => UserActivityColumns,
            _ => SystemErrorColumns
        });
    }

    /// <summary>ツール別利用者数と明細で読み取り方法を切り替えます。</summary>
    public IAsyncEnumerable<ExportRow> ReadAsync(LogKind kind, LogExtraction extraction, CancellationToken ct = default) =>
        kind == LogKind.ToolUsage && extraction == LogExtraction.ToolUserCount ? ReadToolUserCountAsync(ct) : ReadDetailAsync(kind, ct);

    /// <summary>出力開始時の最大主キーを上限とし、その範囲を昇順で読み取ります。</summary>
    private async IAsyncEnumerable<ExportRow> ReadDetailAsync(LogKind kind, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var db = await logs.CreateDbContextAsync(ct);
        switch (kind)
        {
            case LogKind.ToolUsage:
                {
                    var maximum = await db.ToolUsageLogs.AsNoTracking().MaxAsync(x => (long?)x.ToolUsageLogId, ct) ?? 0;
                    var query = db.ToolUsageLogs.AsNoTracking().Where(x => x.ToolUsageLogId <= maximum).OrderBy(x => x.ToolUsageLogId);
                    await foreach (var row in query.AsAsyncEnumerable().WithCancellation(ct))
                        yield return new ExportRow([row.ToolUsageLogId, row.ToolId, row.UserId, row.OccurredAt, row.EventType, row.ResultCode, row.CorrelationId]);
                    break;
                }
            case LogKind.UserActivity:
                {
                    var maximum = await db.UserActivityLogs.AsNoTracking().MaxAsync(x => (long?)x.UserActivityLogId, ct) ?? 0;
                    var query = db.UserActivityLogs.AsNoTracking().Where(x => x.UserActivityLogId <= maximum).OrderBy(x => x.UserActivityLogId);
                    await foreach (var row in query.AsAsyncEnumerable().WithCancellation(ct))
                        yield return new ExportRow([row.UserActivityLogId, row.UserId, row.OccurredAt, row.EventType, row.ResultCode,
                            row.FailureReason, row.RequestPath, row.OperationTargetType, row.OperationTargetId, row.OperationDetails,
                            row.IpAddress, row.CorrelationId]);
                    break;
                }
            default:
                {
                    var maximum = await db.SystemErrorLogs.AsNoTracking().MaxAsync(x => (long?)x.SystemErrorLogId, ct) ?? 0;
                    var query = db.SystemErrorLogs.AsNoTracking().Where(x => x.SystemErrorLogId <= maximum).OrderBy(x => x.SystemErrorLogId);
                    await foreach (var row in query.AsAsyncEnumerable().WithCancellation(ct))
                        yield return new ExportRow([row.SystemErrorLogId, row.ErrorId, row.ErrorCode, row.OccurredAt, row.ApplicationName,
                            row.ToolId, row.UserId, row.ErrorLevel, row.ErrorType, row.ErrorMessage, row.StackTrace, row.RequestPath,
                            row.HttpMethod, row.CorrelationId]);
                    break;
                }
        }
    }

    /// <summary>Toolsを起点に左結合し、未利用・非公開のツールも0人として出力します。</summary>
    private async IAsyncEnumerable<ExportRow> ReadToolUserCountAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await using var log = await logs.CreateDbContextAsync(ct);
        // 集計対象はDESKTOP_DOWNLOAD全件と、WEB_EXECUTEのSUCCESSです。WEB_OPENは加算しません。
        var counts = await log.ToolUsageLogs.AsNoTracking()
            .Where(x => x.EventType == "DESKTOP_DOWNLOAD" || x.EventType == "WEB_EXECUTE" && x.ResultCode == "SUCCESS")
            .Select(x => new { x.ToolId, x.UserId }).Distinct()
            .GroupBy(x => x.ToolId).Select(group => new { ToolId = group.Key, UserCount = group.Count() })
            .ToDictionaryAsync(x => x.ToolId, x => x.UserCount, ct);

        var tools = portal.Tools.AsNoTracking().OrderBy(x => x.ToolId).Select(x => new { x.ToolId, x.ToolName });
        await foreach (var tool in tools.AsAsyncEnumerable().WithCancellation(ct))
            yield return new ExportRow([tool.ToolName, counts.TryGetValue(tool.ToolId, out var count) ? count : 0]);
    }

    /// <summary>種別と出力日時を含むファイル名を作成します。</summary>
    public string BuildFileName(LogKind kind, LogExtraction extraction)
    {
        var name = kind == LogKind.ToolUsage && extraction == LogExtraction.ToolUserCount ? "ツール別利用者数" : kind switch
        {
            LogKind.ToolUsage => "ツール利用ログ",
            LogKind.UserActivity => "ユーザー操作ログ",
            _ => "システムエラーログ"
        };
        return $"{name}_{clock.ToJst(clock.GetUtcNow()):yyyyMMdd_HHmmss}.tsv";
    }
}
