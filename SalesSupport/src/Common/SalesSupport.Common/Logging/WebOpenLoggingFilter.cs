using Microsoft.AspNetCore.Mvc.Filters;

namespace SalesSupport.Common.Logging;

/// <summary>Webツールの入口画面の描画が正常に完了した場合だけ、起動（<c>WEB_OPEN</c>）を記録します。</summary>
/// <remarks>各ツールは入口アクションへ<c>[TypeFilter(typeof(WebOpenLoggingFilter))]</c>で適用し、ツール側へ複製しません。</remarks>
public sealed class WebOpenLoggingFilter(IToolUsageLogger usage) : IAsyncResultFilter
{
    /// <summary>画面応答の完了後に状態を確認し、成功した入口要求を一度記録します。</summary>
    /// <param name="context">描画前の結果です。</param>
    /// <param name="next">画面を描画する後続の処理です。</param>
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var executed = await next();
        if (executed.Canceled || executed.Exception is not null || context.HttpContext.Response.StatusCode is < 200 or >= 300) return;

        await usage.WriteOpenAsync(CancellationToken.None);
    }
}
