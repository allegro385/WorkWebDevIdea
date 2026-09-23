using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Logging;
using SalesSupport.Template.Web.Controllers;
using SalesSupport.Template.Web.Models;
using SalesSupport.Template.Web.Services;
using Xunit;

namespace SalesSupport.Template.Tests;

/// <summary>画面と出力の成否に合わせてツール利用ログを記録することを確認します。</summary>
public sealed class ToolControllerLoggingTests
{
    private static readonly DateOnly Today = new(2026, 9, 23);

    /// <summary>入口画面の準備に失敗した場合は起動成功を記録しません。</summary>
    [Fact]
    public async Task IndexDoesNotLogBeforeViewPreparation()
    {
        var usage = new RecordingUsageLogger();
        var estimates = new StubEstimateService { FailHint = true };
        var controller = CreateController(estimates, usage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Index(CancellationToken.None));
        Assert.Empty(usage.Events);
    }

    /// <summary>TSV生成が失敗した場合は成功ではなく失敗を一度だけ記録します。</summary>
    [Fact]
    public async Task ExecuteLogsFailureWhenDownloadPreparationFails()
    {
        var usage = new RecordingUsageLogger();
        var estimates = new StubEstimateService { FailExport = true };
        var controller = CreateController(estimates, usage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Execute(new EstimateInput { Output = EstimateOutput.Download }, null, CancellationToken.None));
        var entry = Assert.Single(usage.Events);
        Assert.Equal("WEB_EXECUTE", entry.EventType);
        Assert.Equal("FAILURE", entry.ResultCode);
    }

    /// <summary>TSVの内容を生成できた後にだけ実行成功を記録します。</summary>
    [Fact]
    public async Task ExecuteLogsSuccessAfterDownloadPreparation()
    {
        var usage = new RecordingUsageLogger();
        var controller = CreateController(new StubEstimateService(), usage);

        var response = await controller.Execute(new EstimateInput { Output = EstimateOutput.Download }, null, CancellationToken.None);

        Assert.IsType<FileContentResult>(response);
        var entry = Assert.Single(usage.Events);
        Assert.Equal("WEB_EXECUTE", entry.EventType);
        Assert.Equal("SUCCESS", entry.ResultCode);
    }

    /// <summary>入口の応答が成功した後だけ起動ログを記録します。</summary>
    [Theory]
    [InlineData(200, false, true)]
    [InlineData(500, false, false)]
    [InlineData(200, true, false)]
    public async Task WebOpenFilterLogsOnlyCompletedSuccess(int statusCode, bool failed, bool expectedLog)
    {
        var usage = new RecordingUsageLogger();
        var filter = new WebOpenLoggingFilter(usage, Options.Create(new CommonOptions { ToolId = "TEMPLATE" }));
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

        Assert.Equal(expectedLog, usage.Events.Count == 1);
    }

    /// <summary>HTTPコンテキストを備えた検証用Controllerを作成します。</summary>
    private static ToolController CreateController(IEstimateService estimates, IUsageLogger usage) => new(
        estimates, usage, new StubBusinessDateProvider(), Options.Create(new CommonOptions { ToolId = "TEMPLATE" }))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    /// <summary>要求されたイベントだけを記録する検証用ロガーです。</summary>
    private sealed class RecordingUsageLogger : IUsageLogger
    {
        public List<UsageEvent> Events { get; } = [];

        /// <summary>記録内容をテストから確認できるように保持します。</summary>
        public Task<LogWriteResult> WriteAsync(UsageEvent entry, CancellationToken ct = default)
        {
            Events.Add(entry);
            return Task.FromResult(LogWriteResult.Written);
        }
    }

    /// <summary>計算・出力と障害を指定できる検証用サービスです。</summary>
    private sealed class StubEstimateService : IEstimateService
    {
        public bool FailHint { get; init; }
        public bool FailExport { get; init; }

        /// <summary>入口画面の固定選択肢を返します。</summary>
        public IReadOnlyList<EstimateCategory> GetCategories() => [new("STANDARD", "通常", 0)];

        /// <summary>必要な場合に画面案内の取得失敗を再現します。</summary>
        public Task<string> GetDetailFileHintAsync(CancellationToken ct = default) => FailHint
            ? Task.FromException<string>(new InvalidOperationException("条件取得失敗"))
            : Task.FromResult(".tsv");

        /// <summary>固定の計算結果を返します。</summary>
        public Task<EstimateOutcome> CalculateAsync(EstimateRequest request, CancellationToken ct = default) =>
            Task.FromResult(new EstimateOutcome(EstimateOutcomeKind.Calculated,
                new EstimateResult("案件", Today, "通常", 1, 100, 100, 0, 100, null), new ValidationResult([])));

        /// <summary>必要な場合にTSV生成失敗を再現します。</summary>
        public Task<byte[]> WriteTsvAsync(EstimateResult result, CancellationToken ct = default) => FailExport
            ? Task.FromException<byte[]>(new InvalidOperationException("出力失敗"))
            : Task.FromResult(new byte[] { 1 });

        /// <summary>ダウンロード時の固定ファイル名を返します。</summary>
        public string BuildFileName(EstimateResult result) => "estimate.tsv";
    }

    /// <summary>画面の初期日付を固定する検証用時刻プロバイダーです。</summary>
    private sealed class StubBusinessDateProvider : IBusinessDateProvider
    {
        /// <summary>固定の業務日付を返します。</summary>
        public Task<DateOnly> GetTodayAsync(CancellationToken ct = default) => Task.FromResult(Today);
    }
}
