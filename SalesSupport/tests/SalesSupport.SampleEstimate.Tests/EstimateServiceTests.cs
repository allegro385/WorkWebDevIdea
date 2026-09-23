using System.Text;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.DataExport;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Logging;
using SalesSupport.SampleEstimate.Web.Models;
using SalesSupport.SampleEstimate.Web.Services;
using Xunit;

namespace SalesSupport.SampleEstimate.Tests;

/// <summary>入力の再検証、計算、明細ファイルの一時保存と削除、TSV出力を確認します。DB・SMTPへは接続しません。</summary>
public sealed class EstimateServiceTests : IDisposable
{
    private const string ToolId = "SAMPLE-ESTIMATE";
    private static readonly DateOnly Today = new(2026, 9, 22);

    private readonly string temporaryRoot = Directory.CreateTempSubdirectory("tt-temp-").FullName;

    /// <summary>検証用の一時保存領域を削除します。</summary>
    public void Dispose()
    {
        try { Directory.Delete(temporaryRoot, recursive: true); } catch (IOException) { /* 検証環境の残存は無視します。 */ }
    }

    /// <summary>区分の割引率を適用し、小計と合計を算出することを確認します。</summary>
    [Fact]
    public async Task CalculateAppliesCategoryDiscount()
    {
        var service = CreateService();
        var outcome = await service.CalculateAsync(new(Input(quantity: 3, unitPrice: 1000, categoryCode: "CAMPAIGN"), null, null));

        Assert.Equal(EstimateOutcomeKind.Calculated, outcome.Kind);
        var result = Assert.IsType<EstimateResult>(outcome.Result);
        Assert.Equal(3000m, result.Subtotal);
        Assert.Equal(300m, result.DiscountAmount);
        Assert.Equal(2700m, result.Total);
        Assert.Null(result.DetailRowCount);
    }

    /// <summary>画面の入力属性に依存せず、Serviceでも必須・範囲・選択肢を再検証することを確認します。</summary>
    [Fact]
    public async Task CalculateRevalidatesInput()
    {
        var service = CreateService();
        var input = Input(quantity: 0, unitPrice: -1, categoryCode: "UNKNOWN");
        input.ProjectName = "  ";

        var outcome = await service.CalculateAsync(new(input, null, null));

        Assert.Equal(EstimateOutcomeKind.InvalidInput, outcome.Kind);
        Assert.Null(outcome.Result);
        Assert.Contains(outcome.Errors.Errors, error => error.Field == nameof(EstimateInput.ProjectName));
        Assert.Contains(outcome.Errors.Errors, error => error.Field == nameof(EstimateInput.Quantity));
        Assert.Contains(outcome.Errors.Errors, error => error.Field == nameof(EstimateInput.UnitPrice));
        Assert.Contains(outcome.Errors.Errors, error => error.Field == nameof(EstimateInput.CategoryCode));
    }

    /// <summary>適用日を業務日付の前後1年に制限することを確認します。</summary>
    [Theory]
    [InlineData(-400, false)]
    [InlineData(-365, true)]
    [InlineData(0, true)]
    [InlineData(365, true)]
    [InlineData(400, false)]
    public async Task CalculateChecksAppliedOnRange(int offsetDays, bool expected)
    {
        var service = CreateService();
        var input = Input();
        input.AppliedOn = Today.AddDays(offsetDays);

        var outcome = await service.CalculateAsync(new(input, null, null));

        Assert.Equal(expected, outcome.Kind == EstimateOutcomeKind.Calculated);
    }

    /// <summary>明細ファイルの空行を除いた行数を数え、処理後に一時ファイルを残さないことを確認します。</summary>
    [Fact]
    public async Task CalculateCountsDetailRowsAndRemovesTemporaryFile()
    {
        var service = CreateService();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("見出し\n1行目\n\n2行目\n"));

        var outcome = await service.CalculateAsync(new(Input(), content, "detail.tsv"));

        Assert.Equal(3, Assert.IsType<EstimateResult>(outcome.Result).DetailRowCount);
        Assert.Empty(Directory.GetFiles(temporaryRoot, "*", SearchOption.AllDirectories));
    }

    /// <summary>許可されていない拡張子を項目エラーとして返し、一時ファイルを残さないことを確認します。</summary>
    [Fact]
    public async Task CalculateRejectsDetailFileOutsidePolicy()
    {
        var service = CreateService();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("1行目\n"));

        var outcome = await service.CalculateAsync(new(Input(), content, "detail.exe"));

        Assert.Equal(EstimateOutcomeKind.InvalidInput, outcome.Kind);
        Assert.Contains(outcome.Errors.Errors, error => error.Field == EstimateService.DetailFileField);
        Assert.Empty(Directory.GetFiles(temporaryRoot, "*", SearchOption.AllDirectories));
    }

    /// <summary>TSVへ見出しと結果を書き出し、文字列の数式解釈対策を適用することを確認します。</summary>
    [Fact]
    public async Task WriteTsvUsesCommonExportRules()
    {
        var service = CreateService();
        var input = Input();
        input.ProjectName = "=1+1";
        var result = Assert.IsType<EstimateResult>((await service.CalculateAsync(new(input, null, null))).Result);

        var text = Encoding.UTF8.GetString(await service.WriteTsvAsync(result));
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("項目\t値", lines[0].TrimStart('﻿'));
        Assert.Equal("案件名\t'=1+1", lines[1]);
        Assert.Contains("適用日\t2026-09-22", lines);
        Assert.Equal("estimate-20260922.tsv", service.BuildFileName(result));
    }

    /// <summary>アップロード条件から画面案内の文言を組み立てることを確認します。</summary>
    [Fact]
    public async Task DetailFileHintDescribesPolicy()
    {
        var hint = await CreateService().GetDetailFileHintAsync();

        Assert.Contains(".tsv", hint);
        Assert.Contains("1MB", hint);
    }

    /// <summary>実際のファイル保存と区切りテキスト出力を使う検証対象を作成します。</summary>
    /// <returns>一時領域を検証用ディレクトリに向けたServiceです。</returns>
    private IEstimateService CreateService()
    {
        var storageOptions = Options.Create(new StorageOptions { TemporaryRoot = temporaryRoot, CleanupTimeoutSeconds = 5 });
        var policies = new StubPolicyProvider();
        var storage = new FileStorage(storageOptions, policies, new StubErrorLogger());
        return new EstimateService(storage, policies, new DelimitedTextWriter(), new StubBusinessDateProvider(),
            Options.Create(new CommonOptions { ToolId = ToolId, ApplicationName = "見積試算サンプル" }));
    }

    /// <summary>検証で共通に使う妥当な入力を作成します。</summary>
    /// <param name="quantity">数量です。</param>
    /// <param name="unitPrice">単価です。</param>
    /// <param name="categoryCode">区分コードです。</param>
    /// <returns>指定値以外は妥当な入力です。</returns>
    private static EstimateInput Input(int quantity = 2, int unitPrice = 500, string categoryCode = "STANDARD") => new()
    {
        ProjectName = "サンプル案件",
        Quantity = quantity,
        UnitPrice = unitPrice,
        AppliedOn = Today,
        CategoryCode = categoryCode,
        Output = EstimateOutput.Screen
    };

    /// <summary>DBを参照せず固定のアップロード条件を返します。</summary>
    private sealed class StubPolicyProvider : IUploadPolicyProvider
    {
        /// <summary>TSVだけを1MBまで許可する条件を返します。</summary>
        public Task<UploadPolicySnapshot> GetAsync(UploadPurpose purpose, string? toolId = null, CancellationToken ct = default) =>
            Task.FromResult(new UploadPolicySnapshot(1, purpose, toolId, 1024 * 1024, [".tsv"]));
    }

    /// <summary>DBを参照せず固定の業務日付を返します。</summary>
    private sealed class StubBusinessDateProvider : IBusinessDateProvider
    {
        /// <summary>検証で基準にする業務日付を返します。</summary>
        public Task<DateOnly> GetTodayAsync(CancellationToken ct = default) => Task.FromResult(Today);
    }

    /// <summary>清掃失敗の記録要求を受け取るだけの代替です。</summary>
    private sealed class StubErrorLogger : ISystemErrorLogger
    {
        /// <summary>常に成功を返します。</summary>
        public Task<LogWriteResult> WriteAsync(SystemErrorEvent entry, CancellationToken ct = default) =>
            Task.FromResult(LogWriteResult.Written);
    }
}
