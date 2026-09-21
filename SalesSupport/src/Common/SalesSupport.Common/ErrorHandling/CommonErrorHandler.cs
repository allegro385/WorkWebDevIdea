using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Data;
using SalesSupport.Common.Logging;

namespace SalesSupport.Common.ErrorHandling;

/// <summary>登録コードまたはツール番号で障害を識別します。関連情報はログ専用です。</summary>
public sealed record ErrorRequest(string? ErrorCode = null, string? ToolId = null, int? ErrorNo = null, Exception? Exception = null,
    int StatusCode = 500, IReadOnlyDictionary<string, string>? Details = null);
/// <summary>安全な表示と一度だけの記録を行います。</summary>
public interface IErrorHandler
{
    /// <summary>未登録やDB障害でも一般エラーを返します。</summary>
    Task<ErrorPresentation> HandleAsync(ErrorRequest request, CancellationToken ct = default);
}
/// <summary>表示用定義を検索し、ログ側へ一方向に通知します。</summary>
public sealed class CommonErrorHandler(IDbContextFactory<CommonDbContext> factory, ISystemErrorLogger logger) : IErrorHandler
{
    private static readonly string[] AllowedDetailKeys = ["処理段階", "行番号", "件数", "項目", "対象", "理由"];
    private static readonly string[] SecretPatterns = ["password", "passwd", "pwd", "secret", "token", "credential", "data source", "initial catalog", "authorization"];

    /// <summary>例外や秘密値を表示へ混入させません。</summary>
    public async Task<ErrorPresentation> HandleAsync(ErrorRequest request, CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        var message = "処理中にエラーが発生しました。管理者へお問い合わせください。";
        string? code = null;
        var level = "ERROR";
        if (request.ErrorCode is not null && request.ToolId is null && request.ErrorNo is null || request.ErrorCode is null && request.ToolId is not null && request.ErrorNo is not null)
        {
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                var query = db.ErrorCodes.AsNoTracking();
                var entry = request.ErrorCode is not null ? await query.SingleOrDefaultAsync(x => x.ErrorCode == request.ErrorCode, ct)
                    : await query.SingleOrDefaultAsync(x => x.ToolId == request.ToolId && x.ErrorNo == request.ErrorNo, ct);
                if (entry is not null && !string.IsNullOrWhiteSpace(entry.DisplayMessage) && entry.ErrorLevel is "ERROR" or "CRITICAL")
                {
                    message = entry.DisplayMessage;
                    code = entry.ErrorCode;
                    level = entry.ErrorLevel;
                }
            }
            catch (Exception) { /* 表示用DBも利用できない場合は固定文言を維持します。 */ }
        }
        await logger.WriteAsync(new SystemErrorEvent(id, code, message + Format(request.Details), request.Exception, level), ct);
        return new(id, message, request.StatusCode is 400 or 409 or 500 or 503 ? request.StatusCode : 500);
    }

    /// <summary>許可キーだけを`[名称：値]`へ整形し、未知のキーと秘密らしい値を捨てます。</summary>
    private static string Format(IReadOnlyDictionary<string, string>? details)
    {
        if (details is null || details.Count == 0) return "";
        var parts = AllowedDetailKeys.Where(details.ContainsKey).Select(key => (Key: key, Value: Safe(details[key]))).Where(x => x.Value.Length > 0);
        return string.Concat(parts.Select(x => $"[{x.Key}：{x.Value}]"));
    }

    /// <summary>制御文字と既知の秘密パターンを除き、列長に収まる長さへ切り詰めます。</summary>
    private static string Safe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) return "";
        if (SecretPatterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase))) return "";
        return value[..Math.Min(value.Length, 100)];
    }
}
/// <summary>認証より前に例外境界を配置するホスト用拡張です。</summary>
public static class CommonErrorExtensions
{
    /// <summary>共通処理の例外を秘密値を含まない応答へ変換します。</summary>
    public static IApplicationBuilder UseSalesSupportErrors(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        _ = context.RequestServices.GetRequiredService<RequestCorrelation>();
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); }
        catch (Exception exception)
        {
            var dependency = exception is ConfigurationException or Microsoft.Data.SqlClient.SqlException;
            var error = await context.RequestServices.GetRequiredService<IErrorHandler>().HandleAsync(new(Exception: exception, StatusCode: dependency ? 503 : 500));
            if (context.Response.HasStarted) { context.Abort(); return; }
            context.Response.Clear();
            context.Response.StatusCode = error.StatusCode;
            await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = error.StatusCode, Title = error.Message, Extensions = { ["errorId"] = error.ErrorId }
            });
        }
    });
}
