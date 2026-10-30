using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.ErrorHandling;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>共通例外処理の応答を、画面要求では共通エラー画面、API要求ではProblemDetailsへ書き分けることを確認します。</summary>
public sealed class CommonErrorResponseTests
{
    private static readonly ErrorPresentation Error = new(Guid.Parse("11111111-2222-3333-4444-555555555555"), "処理中にエラーが発生しました。", 500);

    /// <summary>画面要求では共通エラーViewへ表示文言と問い合わせ番号を渡します。</summary>
    [Fact]
    public async Task RendersCommonErrorViewForScreenRequest()
    {
        var executor = new CapturingViewExecutor();
        var context = CreateContext("/tools/T001", executor);

        await CommonErrorResponse.WriteAsync(context, Error);

        Assert.Equal("CommonError", executor.Executed?.ViewName);
        Assert.Same(Error, executor.Executed?.ViewData.Model);
        Assert.Equal(500, context.Response.StatusCode);
    }

    /// <summary>API要求ではViewを描画せず、安全なProblemDetailsとして返します。</summary>
    [Fact]
    public async Task WritesProblemDetailsForApiRequest()
    {
        var executor = new CapturingViewExecutor();
        var context = CreateContext("/api/users/me", executor);

        await CommonErrorResponse.WriteAsync(context, Error);

        Assert.Null(executor.Executed);
        var body = await ReadBodyAsync(context);
        Assert.Equal(Error.Message, body.GetProperty("title").GetString());
        Assert.Equal(Error.ErrorId, body.GetProperty("errorId").GetGuid());
        Assert.Equal(500, context.Response.StatusCode);
    }

    /// <summary>画面を描画できない場合も応答を二重に作らず、ProblemDetailsへ退避します。</summary>
    [Fact]
    public async Task FallsBackToProblemDetailsWhenViewCannotBeRendered()
    {
        var context = CreateContext("/tools/T001", new FailingViewExecutor());

        await CommonErrorResponse.WriteAsync(context, Error);

        var body = await ReadBodyAsync(context);
        Assert.Equal(Error.ErrorId, body.GetProperty("errorId").GetGuid());
        Assert.Equal(500, context.Response.StatusCode);
    }

    /// <summary>応答本文を読み取れる要求を作成します。</summary>
    /// <param name="path">要求のパスです。`/api`で始まるかどうかで応答形式が変わります。</param>
    /// <param name="executor">View描画の代替です。</param>
    /// <returns>検証用のHTTPコンテキストです。</returns>
    private static DefaultHttpContext CreateContext(string path, IActionResultExecutor<ViewResult> executor)
    {
        var services = new ServiceCollection();
        services.AddSingleton(executor);
        services.AddSingleton<IModelMetadataProvider, EmptyModelMetadataProvider>();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>応答本文をJSONとして読み取ります。</summary>
    /// <param name="context">読み取る要求です。</param>
    /// <returns>応答本文のJSONです。</returns>
    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }

    /// <summary>描画せずにViewResultだけを記録する代替です。</summary>
    private sealed class CapturingViewExecutor : IActionResultExecutor<ViewResult>
    {
        /// <summary>実行を求められたViewResultです。未実行ではnullです。</summary>
        public ViewResult? Executed { get; private set; }

        /// <summary>要求されたViewResultを記録します。</summary>
        public Task ExecuteAsync(ActionContext context, ViewResult result)
        {
            Executed = result;
            context.HttpContext.Response.StatusCode = result.StatusCode ?? context.HttpContext.Response.StatusCode;
            return Task.CompletedTask;
        }
    }

    /// <summary>Viewを見つけられない状況を再現する代替です。</summary>
    private sealed class FailingViewExecutor : IActionResultExecutor<ViewResult>
    {
        /// <summary>描画の失敗を再現します。</summary>
        public Task ExecuteAsync(ActionContext context, ViewResult result) => throw new InvalidOperationException("Viewを見つけられません。");
    }
}
