using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Logging;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>Commonが提供する入口ログのフィルターが、画面の描画を完了した成功応答だけを<c>WEB_OPEN</c>として1件記録することを確認します。</summary>
public sealed class WebOpenLoggingFilterTests
{
    /// <summary>入口の応答が成功した後だけ起動ログを記録します。失敗応答と描画中の例外は記録しません。</summary>
    /// <param name="statusCode">応答の状態コードです。</param>
    /// <param name="failed">描画中に例外が起きた場合はtrueです。</param>
    /// <param name="expectedLog">起動ログを1件記録する場合はtrueです。</param>
    [Theory]
    [InlineData(200, false, true)]
    [InlineData(500, false, false)]
    [InlineData(200, true, false)]
    public async Task LogsOnlyCompletedSuccess(int statusCode, bool failed, bool expectedLog)
    {
        var usage = new RecordingUsageLogger();
        var filter = new WebOpenLoggingFilter(new ToolUsageLogger(usage, Options.Create(new CommonOptions { ToolId = "T001" })));
        var http = new DefaultHttpContext();
        http.Response.StatusCode = statusCode;
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        List<IFilterMetadata> filters = [];
        var result = new EmptyResult();
        var executing = new ResultExecutingContext(action, filters, result, new object());

        await filter.OnResultExecutionAsync(executing, () =>
        {
            var executed = new ResultExecutedContext(action, filters, result, new object());
            if (failed) executed.Exception = new InvalidOperationException("描画失敗");
            return Task.FromResult(executed);
        });

        UsageEvent[] expected = expectedLog ? [new UsageEvent("T001", "WEB_OPEN", "SUCCESS")] : [];
        Assert.Equal(expected, usage.Events);
    }
}
