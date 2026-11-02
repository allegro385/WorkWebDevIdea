using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Logging;
using SalesSupport.Common.UI;
using SalesSupport.Template.Web.Controllers;
using SalesSupport.Template.Web.TemplateExample;
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
        var examples = new StubExampleService { FailHint = true };
        var controller = CreateController(examples, usage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Index(CancellationToken.None));
        Assert.Empty(usage.Events);
    }

    /// <summary>TSV生成が失敗した場合は成功ではなく失敗を一度だけ記録します。</summary>
    [Fact]
    public async Task ExecuteLogsFailureWhenDownloadPreparationFails()
    {
        var usage = new RecordingUsageLogger();
        var examples = new StubExampleService { FailExport = true };
        var controller = CreateController(examples, usage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Execute(new ExampleInput { Output = ExampleOutput.Download }, null, CancellationToken.None));
        var entry = Assert.Single(usage.Events);
        Assert.Equal("WEB_EXECUTE", entry.EventType);
        Assert.Equal("FAILURE", entry.ResultCode);
    }

    /// <summary>TSVの内容を生成できた後にだけ実行成功を記録します。</summary>
    [Fact]
    public async Task ExecuteLogsSuccessAfterDownloadPreparation()
    {
        var usage = new RecordingUsageLogger();
        var controller = CreateController(new StubExampleService(), usage);

        var response = await controller.Execute(new ExampleInput { Output = ExampleOutput.Download }, null, CancellationToken.None);

        Assert.IsType<FileContentResult>(response);
        var entry = Assert.Single(usage.Events);
        Assert.Equal("WEB_EXECUTE", entry.EventType);
        Assert.Equal("SUCCESS", entry.ResultCode);
    }

    /// <summary>Serviceの項目エラーを入力欄の`Input.`付きキーへ、明細ファイルのエラーはファイル欄のキーへ結び付けます。</summary>
    [Fact]
    public async Task ExecuteDisplaysServiceErrorsAtInputFields()
    {
        var usage = new RecordingUsageLogger();
        var controller = CreateController(new StubExampleService { InvalidResult = true }, usage);

        var response = await controller.Execute(new ExampleInput(), null, CancellationToken.None);

        Assert.IsType<ViewResult>(response);
        Assert.True(controller.ModelState.ContainsKey("Input.Quantity"));
        Assert.True(controller.ModelState.ContainsKey(ExampleService.DetailFileField));
        Assert.Equal("FAILURE", Assert.Single(usage.Events).ResultCode);
    }

    /// <summary>HTTPコンテキストを備えた検証用Controllerを作成します。</summary>
    private static ToolController CreateController(IExampleService examples, IUsageLogger usage) => new(
        examples, new ToolUsageLogger(usage, Options.Create(new CommonOptions { ToolId = "TEMPLATE" })), new StubBusinessDateProvider(),
        new ToolPageShell(Options.Create(new CommonOptions { ApplicationName = "テンプレートツール" })))
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
    private sealed class StubExampleService : IExampleService
    {
        public bool FailHint { get; init; }
        public bool FailExport { get; init; }
        public bool InvalidResult { get; init; }

        /// <summary>入口画面の固定選択肢を返します。</summary>
        public IReadOnlyList<ExampleCategory> GetCategories() => [new("STANDARD", "通常", 0)];

        /// <summary>必要な場合に画面案内の取得失敗を再現します。</summary>
        public Task<DetailFileHint> GetDetailFileHintAsync(CancellationToken ct = default) => FailHint
            ? Task.FromException<DetailFileHint>(new InvalidOperationException("条件取得失敗"))
            : Task.FromResult(new DetailFileHint(true, ".tsv"));

        /// <summary>固定の計算結果、または入力項目と明細ファイルの項目エラーを返します。</summary>
        public Task<ExampleOutcome> CalculateAsync(ExampleRequest request, CancellationToken ct = default) =>
            Task.FromResult(InvalidResult
                ? new ExampleOutcome(ExampleOutcomeKind.InvalidInput, null, new ValidationResult([
                    new FieldError(nameof(ExampleInput.Quantity), "OUT_OF_RANGE", "数量が不正です。"),
                    new FieldError(ExampleService.DetailFileField, "INVALID_FILE", "明細ファイルが不正です。")]))
                : new ExampleOutcome(ExampleOutcomeKind.Calculated,
                    new ExampleResult("案件", Today, "通常", 1, 100, 100, 0, 100, null), new ValidationResult([])));

        /// <summary>必要な場合にTSV生成失敗を再現します。</summary>
        public Task<byte[]> WriteTsvAsync(ExampleResult result, CancellationToken ct = default) => FailExport
            ? Task.FromException<byte[]>(new InvalidOperationException("出力失敗"))
            : Task.FromResult(new byte[] { 1 });

        /// <summary>ダウンロード時の固定ファイル名を返します。</summary>
        public string BuildFileName(ExampleResult result) => "example.tsv";
    }

    /// <summary>画面の初期日付を固定する検証用時刻プロバイダーです。</summary>
    private sealed class StubBusinessDateProvider : IBusinessDateProvider
    {
        /// <summary>固定の業務日付を返します。</summary>
        public Task<DateOnly> GetTodayAsync(CancellationToken ct = default) => Task.FromResult(Today);
    }
}
