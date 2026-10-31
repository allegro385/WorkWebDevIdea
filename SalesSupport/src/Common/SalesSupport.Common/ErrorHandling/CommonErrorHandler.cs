using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
public sealed class CommonErrorHandler(IDbContextFactory<CommonDbContext> factory, ISystemErrorLogger logger, IOptions<CommonOptions> options,
    ILogger<CommonErrorHandler> console) : IErrorHandler
{

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
        await logger.WriteAsync(new SystemErrorEvent(id, code, Exception: request.Exception, ErrorLevel: level, Details: request.Details), ct);
        // 単独開発（開発環境に限定）では障害をDBへ記録しないため、原因を追えるよう例外の詳細をホストのログ（コンソール）へも出力します。
        if (options.Value.IsStandalone) console.LogError(request.Exception, "処理中にエラーが発生しました。ErrorId={ErrorId}, ErrorCode={ErrorCode}", id, code);
        return new(id, message, request.StatusCode is 400 or 409 or 500 or 503 ? request.StatusCode : 500);
    }

}

/// <summary>共通エラーの応答を、画面要求とAPI要求へ書き分けます。</summary>
public static class CommonErrorResponse
{
    /// <summary>共通エラー画面のView名です。CommonのViews/Sharedが提供します。</summary>
    public const string ViewName = "CommonError";

    /// <summary>ProblemDetailsで返すAPI要求かどうかを判定します。</summary>
    /// <param name="request">判定する要求です。</param>
    /// <returns>`/api`で始まる要求ならtrueです。</returns>
    public static bool IsApiRequest(HttpRequest request) => request.Path.StartsWithSegments("/api");

    /// <summary>画面要求は共通エラー画面、API要求はProblemDetailsで応答します。</summary>
    /// <param name="context">応答を書き込む要求です。</param>
    /// <param name="error">表示する文言と問い合わせ番号です。</param>
    public static async Task WriteAsync(HttpContext context, ErrorPresentation error)
    {
        context.Response.StatusCode = error.StatusCode;
        if (!IsApiRequest(context.Request))
        {
            // 画面を描画できない場合も応答を二重に作らず、ProblemDetailsへ退避します。
            try
            {
                await RenderViewAsync(context, error);
                return;
            }
            catch (Exception) when (!context.Response.HasStarted) { }
        }
        await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = error.StatusCode, Title = error.Message, Extensions = { ["errorId"] = error.ErrorId }
        });
    }

    /// <summary>共通レイアウトを使う共通エラー画面を描画します。</summary>
    /// <param name="context">応答を書き込む要求です。</param>
    /// <param name="error">Viewへ渡す表示情報です。</param>
    private static Task RenderViewAsync(HttpContext context, ErrorPresentation error)
    {
        var services = context.RequestServices;
        var viewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary<ErrorPresentation>(
            services.GetRequiredService<Microsoft.AspNetCore.Mvc.ModelBinding.IModelMetadataProvider>(),
            new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary()) { Model = error };
        var result = new Microsoft.AspNetCore.Mvc.ViewResult { ViewName = ViewName, ViewData = viewData, StatusCode = error.StatusCode };
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(context, context.GetRouteData(),
            new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        return services.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.IActionResultExecutor<Microsoft.AspNetCore.Mvc.ViewResult>>()
            .ExecuteAsync(actionContext, result);
    }
}

/// <summary>認証より前に例外境界を配置するホスト用拡張です。</summary>
public static class CommonErrorExtensions
{
    /// <summary>共通処理の例外を秘密値を含まない応答へ変換します。</summary>
    internal static IApplicationBuilder UseSalesSupportErrors(this IApplicationBuilder app) => app.Use(async (context, next) =>
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
            await CommonErrorResponse.WriteAsync(context, error);
        }
    });
}
