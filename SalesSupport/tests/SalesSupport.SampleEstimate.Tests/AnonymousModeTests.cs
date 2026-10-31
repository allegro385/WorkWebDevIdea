using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Logging;
using SalesSupport.Common.UI;
using SalesSupport.SampleEstimate.Web.Controllers;
using SalesSupport.SampleEstimate.Web.Data;
using SalesSupport.SampleEstimate.Web.Models;
using SalesSupport.SampleEstimate.Web.Services;
using Xunit;

namespace SalesSupport.SampleEstimate.Tests;

/// <summary>匿名モードでは本人を特定できないため、案件の保存・一覧を画面とサーバーの双方で提供しないことを確認します。</summary>
public sealed class AnonymousModeTests
{
    /// <summary>匿名モードの保存要求はDBへ書かず、入力画面へ案内を返します。</summary>
    [Fact]
    public async Task ExecuteRejectsSaveInAnonymousMode()
    {
        var records = new CountingRecordService();
        var controller = CreateToolController(Anonymous(), records);

        var response = await controller.Execute(
            new EstimateInput { Output = EstimateOutput.Save, SubmissionId = Guid.NewGuid(), CategoryCode = "STANDARD" }, null, CancellationToken.None);

        Assert.IsType<ViewResult>(response);
        Assert.Equal(0, records.CreateCalls);
        Assert.True(controller.ModelState.ContainsKey("Input.Output"));
    }

    /// <summary>本人を特定できる通常構成では保存を受け付けます。</summary>
    [Fact]
    public async Task ExecuteAllowsSaveWhenUserIsIdentified()
    {
        var records = new CountingRecordService();
        var controller = CreateToolController(Portal(), records);

        var response = await controller.Execute(
            new EstimateInput { Output = EstimateOutput.Save, SubmissionId = Guid.NewGuid(), CategoryCode = "STANDARD" }, null, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(response);
        Assert.Equal(1, records.CreateCalls);
    }

    /// <summary>匿名モードでは案件一覧の全アクションを対象なしとして拒否します。</summary>
    [Fact]
    public void RecordsScreenIsRejectedInAnonymousMode()
    {
        var controller = CreateRecordsController(Anonymous());
        var context = CreateActionContext(controller);

        controller.OnActionExecuting(context);

        Assert.IsType<NotFoundResult>(context.Result);
    }

    /// <summary>本人を特定できる通常構成では案件一覧を通常どおり処理します。</summary>
    [Fact]
    public void RecordsScreenIsAvailableWhenUserIsIdentified()
    {
        var controller = CreateRecordsController(Portal());
        var context = CreateActionContext(controller);

        controller.OnActionExecuting(context);

        Assert.Null(context.Result);
    }

    /// <summary>ツール起動の記録はツールの入口だけに適用し、ツール内の画面遷移では記録しません。</summary>
    [Fact]
    public void WebOpenIsRecordedOnlyAtToolEntry()
    {
        Assert.True(HasWebOpenFilter(typeof(ToolController).GetMethod(nameof(ToolController.Index))!));
        foreach (var action in typeof(RecordsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            Assert.False(HasWebOpenFilter(action));
    }

    /// <summary>対象のアクションへ起動ログのフィルターが適用されているかを返します。</summary>
    /// <param name="action">確認するアクションです。</param>
    /// <returns>適用されていればtrueです。</returns>
    private static bool HasWebOpenFilter(MethodInfo action) =>
        action.GetCustomAttributes<TypeFilterAttribute>().Any(x => x.ImplementationType == typeof(WebOpenLoggingFilter));

    /// <summary>匿名モードの設定です。</summary>
    /// <returns>ログインを行わない匿名モードの共通設定です。</returns>
    private static CommonOptions Anonymous() => new()
    {
        Kind = ApplicationKind.Tool, EnvironmentCode = "DEVELOPMENT", ToolId = "SAMPLE-ESTIMATE", ApplicationName = "見積試算",
        ExecutionMode = ToolExecutionMode.Portal, AuthenticationMode = SiteAuthenticationMode.Anonymous
    };

    /// <summary>共有Cookieで本人を認証する通常構成の設定です。</summary>
    /// <returns>メールOTPで本人を認証する共通設定です。</returns>
    private static CommonOptions Portal() => new()
    {
        Kind = ApplicationKind.Tool, EnvironmentCode = "DEVELOPMENT", ToolId = "SAMPLE-ESTIMATE", ApplicationName = "見積試算",
        ExecutionMode = ToolExecutionMode.Portal, AuthenticationMode = SiteAuthenticationMode.EmailOtp
    };

    /// <summary>HTTPコンテキストを備えた検証用の入口Controllerを作成します。</summary>
    /// <param name="options">適用する起動・認証モードです。</param>
    /// <param name="records">保存要求を数える代替です。</param>
    /// <returns>検証用のControllerです。</returns>
    private static ToolController CreateToolController(CommonOptions options, CountingRecordService records) => new(
        new StubEstimateService(), records, new StubToolStatusService(),
        new ToolUsageLogger(new SkippingUsageLogger(), Options.Create(options)), new StubBusinessDateProvider(),
        new ToolPageShell(Options.Create(options)), Options.Create(options))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    /// <summary>検証用の案件一覧Controllerを作成します。</summary>
    /// <param name="options">適用する起動・認証モードです。</param>
    /// <returns>検証用のControllerです。</returns>
    private static RecordsController CreateRecordsController(CommonOptions options) => new(
        new CountingRecordService(), new StubEstimateService(),
        new ToolUsageLogger(new SkippingUsageLogger(), Options.Create(options)),
        new ToolPageShell(Options.Create(options)), Options.Create(options))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    /// <summary>アクション実行前のフィルター検証用コンテキストを作成します。</summary>
    /// <param name="controller">対象のControllerです。</param>
    /// <returns>結果が未設定のコンテキストです。</returns>
    private static ActionExecutingContext CreateActionContext(Controller controller) => new(
        new ActionContext(controller.ControllerContext.HttpContext, new RouteData(), new ControllerActionDescriptor()),
        [], new Dictionary<string, object?>(), controller);

    /// <summary>保存要求の回数だけを数える代替です。</summary>
    private sealed class CountingRecordService : IEstimateRecordService
    {
        /// <summary>保存を要求された回数です。</summary>
        public int CreateCalls { get; private set; }

        /// <summary>空の一覧を返します。</summary>
        public Task<EstimateRecordList> ListAsync(DateOnly? from, DateOnly? to, string? categoryCode, CancellationToken ct = default) =>
            Task.FromResult(new EstimateRecordList([], 0, 0));

        /// <summary>保存済み案件がない状態を返します。</summary>
        public Task<EstimateRecord?> GetAsync(Guid recordId, CancellationToken ct = default) => Task.FromResult<EstimateRecord?>(null);

        /// <summary>保存回数を数え、固定IDを返します。</summary>
        public Task<Guid> CreateAsync(Guid submissionId, EstimateInput input, EstimateResult result, CancellationToken ct = default)
        {
            CreateCalls++;
            return Task.FromResult(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        }

        /// <summary>更新が成功した結果を返します。</summary>
        public Task<RecordUpdateResult> UpdateAsync(Guid recordId, int updateCount, EstimateInput input, EstimateResult result, CancellationToken ct = default) =>
            Task.FromResult(RecordUpdateResult.Updated);
    }

    /// <summary>DBを使わず公開状態の表示名を返します。</summary>
    private sealed class StubToolStatusService : IToolStatusService
    {
        /// <summary>固定の表示名を返します。</summary>
        public Task<string> GetStatusNameAsync(CancellationToken ct = default) => Task.FromResult("限定公開");
    }

    /// <summary>ログを記録しない代替です。</summary>
    private sealed class SkippingUsageLogger : IUsageLogger
    {
        /// <summary>記録を行わずに完了を返します。</summary>
        public Task<LogWriteResult> WriteAsync(UsageEvent entry, CancellationToken ct = default) => Task.FromResult(LogWriteResult.Skipped);
    }

    /// <summary>画面の初期日付を固定する代替です。</summary>
    private sealed class StubBusinessDateProvider : IBusinessDateProvider
    {
        /// <summary>固定の業務日付を返します。</summary>
        public Task<DateOnly> GetTodayAsync(CancellationToken ct = default) => Task.FromResult(new DateOnly(2026, 9, 23));
    }

    /// <summary>計算と出力を固定で返す代替です。</summary>
    private sealed class StubEstimateService : IEstimateService
    {
        /// <summary>入口画面の固定選択肢を返します。</summary>
        public IReadOnlyList<EstimateCategory> GetCategories() => [new("STANDARD", "通常", 0)];

        /// <summary>固定の案内を返します。</summary>
        public Task<DetailFileHint> GetDetailFileHintAsync(CancellationToken ct = default) => Task.FromResult(new DetailFileHint(true, ".tsv"));

        /// <summary>固定の計算結果を返します。</summary>
        public Task<EstimateOutcome> CalculateAsync(EstimateRequest request, CancellationToken ct = default) =>
            Task.FromResult(new EstimateOutcome(EstimateOutcomeKind.Calculated,
                new EstimateResult("案件", new DateOnly(2026, 9, 23), "通常", 1, 100, 100, 0, 100, null), new ValidationResult([])));

        /// <summary>固定のTSVを返します。</summary>
        public Task<byte[]> WriteTsvAsync(EstimateResult result, CancellationToken ct = default) => Task.FromResult(new byte[] { 1 });

        /// <summary>ダウンロード時の固定ファイル名を返します。</summary>
        public string BuildFileName(EstimateResult result) => "estimate.tsv";
    }
}
