using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Logging;
using SalesSupport.Common.UI;
using SalesSupport.SampleEstimate.Web.Models;
using SalesSupport.SampleEstimate.Web.Services;

namespace SalesSupport.SampleEstimate.Web.Controllers;

/// <summary>見積試算の入口です。HTTPと画面遷移を担当し、計算・保存はServiceへ委譲します。</summary>
/// <remarks>利用可否の判定は共通の既定認可方針が行うため、この画面で独自の認証・権限確認は実装しません。</remarks>
[Route("")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ToolController(IEstimateService estimates, IEstimateRecordService records, IToolStatusService status, IToolUsageLogger usage, IBusinessDateProvider businessDate,
    IToolPageShell shell, IOptions<CommonOptions> options) : Controller
{
    /// <summary>入力不正のときに画面上部へ表示する案内です。</summary>
    private static readonly OperationMessage InvalidInputMessage =
        new(OperationMessageKind.Warning, "入力内容を確認してください。修正後にもう一度実行できます。");

    /// <summary>入力画面を表示し、入口の利用を一度だけ記録します。</summary>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>初期値を設定した入力画面です。</returns>
    [HttpGet("")]
    [TypeFilter(typeof(WebOpenLoggingFilter))]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var input = new EstimateInput
        {
            SubmissionId = Guid.NewGuid(),
            AppliedOn = await businessDate.GetTodayAsync(ct),
            CategoryCode = estimates.GetCategories()[0].Code
        };
        return View(await BuildViewAsync(input, null, ct));
    }

    /// <summary>入力を検証して実行し、選択された方法で結果を返します。</summary>
    /// <param name="input">入力画面の項目です。</param>
    /// <param name="detailFile">任意の明細ファイル1件です。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>結果画面、TSVのダウンロード、または入力エラーを表示した入力画面です。</returns>
    /// <remarks>要求サイズの上限はパイプラインの保護であり、業務上の容量条件はアップロード条件で検証します。</remarks>
    [HttpPost("execute")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Execute(EstimateInput input, IFormFile? detailFile, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await FailureViewAsync(input, ct);
        // 本人を特定できない構成（単独開発・匿名モード）では案件の所有者を決められないため保存を拒否します。
        if (!options.Value.HasIdentifiedUser && input.Output == EstimateOutput.Save)
        {
            ModelState.AddModelError("Input.Output", "案件の保存にはPortalでログインしてください（単独開発とログインなしの公開では保存できません）。");
            return await FailureViewAsync(input, ct);
        }
        if (input.Output == EstimateOutput.Save && input.SubmissionId == Guid.Empty)
        {
            ModelState.AddModelError("Input.SubmissionId", "保存の送信IDがありません。入力画面からやり直してください。");
            return await FailureViewAsync(input, ct);
        }

        EstimateOutcome outcome;
        IActionResult? preparedResult = null;
        try
        {
            await using var content = detailFile?.OpenReadStream();
            outcome = await estimates.CalculateAsync(new EstimateRequest(input, content, detailFile?.FileName), ct);
            if (outcome.Result is { } result)
            {
                // 出力の生成が終わるまで成功ログを記録しません。結果ファイルは保存せず、POSTの応答で返します。
                if (input.Output == EstimateOutput.Save)
                {
                    var recordId = await records.CreateAsync(input.SubmissionId, input, result, ct);
                    preparedResult = RedirectToAction("Details", "Records", new { id = recordId });
                }
                else if (input.Output == EstimateOutput.Download)
                    preparedResult = File(await estimates.WriteTsvAsync(result, ct), "text/tab-separated-values", estimates.BuildFileName(result));
                else
                {
                    ViewData.SetPageShell(shell.Build("計算結果"));
                    preparedResult = View("Result", result);
                }
            }
        }
        catch
        {
            await usage.WriteExecuteAsync(succeeded: false, CancellationToken.None);
            throw;
        }

        if (outcome.Result is null)
        {
            // 入力項目は画面の`Input.項目名`へ、別引数で受け取る明細ファイルはファイル欄の名前へ結び付けます。
            ModelState.AddValidationResult(outcome.Errors, nameof(EstimateViewModel.Input), EstimateService.DetailFileField);
            return await FailureViewAsync(input, ct);
        }

        await usage.WriteExecuteAsync(succeeded: true, ct);
        return preparedResult!;
    }

    /// <summary>入力不正を一度だけ記録し、入力内容を保持したまま入力画面へ戻します。</summary>
    /// <param name="input">再表示する入力です。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>項目エラーを表示した入力画面です。</returns>
    private async Task<IActionResult> FailureViewAsync(EstimateInput input, CancellationToken ct)
    {
        await usage.WriteExecuteAsync(succeeded: false, ct);
        return View("Index", await BuildViewAsync(input, InvalidInputMessage, ct));
    }

    /// <summary>入力画面の表示情報を組み立てます。ファイル選択は再表示しません。</summary>
    /// <param name="input">表示する入力です。</param>
    /// <param name="message">画面上部へ表示する案内です。表示しない場合はNULLです。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>入力画面の表示情報です。</returns>
    private async Task<EstimateViewModel> BuildViewAsync(EstimateInput input, OperationMessage? message, CancellationToken ct)
    {
        // パンくずの上位（Portalのトップ・ツール一覧）と対象ツール名はCommonが付けます。
        ViewData.SetPageShell(shell.Build("入力"));
        return new EstimateViewModel
        {
            Input = input,
            ToolStatusName = await status.GetStatusNameAsync(ct),
            Categories = estimates.GetCategories(),
            DetailFile = await estimates.GetDetailFileHintAsync(ct),
            Message = message
        };
    }
}
