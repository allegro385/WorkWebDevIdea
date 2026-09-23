using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Logging;

namespace SalesSupport.SampleEstimate.Web.Controllers;

/// <summary>入口画面の描画が正常に完了した場合だけ、Webツールの起動を記録します。</summary>
public sealed class WebOpenLoggingFilter(IUsageLogger usage, IOptions<CommonOptions> options) : IAsyncResultFilter
{
    /// <summary>画面応答の完了後に状態を確認し、成功した入口要求を一度記録します。</summary>
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var executed = await next();
        if (executed.Canceled || executed.Exception is not null || context.HttpContext.Response.StatusCode is < 200 or >= 300) return;

        await usage.WriteAsync(new UsageEvent(options.Value.ToolId ?? "", "WEB_OPEN", "SUCCESS"), CancellationToken.None);
    }
}
