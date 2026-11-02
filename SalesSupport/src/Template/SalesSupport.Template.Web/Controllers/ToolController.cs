using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Logging;
using SalesSupport.Common.UI;
using SalesSupport.Template.Web.TemplateExample;

namespace SalesSupport.Template.Web.Controllers;

/// <summary>テンプレートツールの入口です。HTTPの受付と画面遷移だけを担当し、計算はServiceへ委譲します。</summary>
/// <remarks>利用可否の判定は共通の既定認可方針が行うため、この画面で独自の認証・権限確認は実装しません。</remarks>
[Route("")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ToolController(IExampleService examples, IToolUsageLogger usage, IBusinessDateProvider businessDate,
    IToolPageShell shell) : Controller
{
    /// <summary>題材の入力画面のView名です。題材を削除したら自分の画面名へ変更します。</summary>
    private const string ExampleIndexView = "TemplateExampleIndex";

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
        var input = new ExampleInput
        {
            AppliedOn = await businessDate.GetTodayAsync(ct),
            CategoryCode = examples.GetCategories()[0].Code
        };
        return View(ExampleIndexView, await BuildViewAsync(input, null, ct));
    }

    /// <summary>入力を検証して実行し、選択された方法で結果を返します。</summary>
    /// <param name="input">入力画面の項目です。</param>
    /// <param name="detailFile">任意の明細ファイル1件です。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>結果画面、TSVのダウンロード、または入力エラーを表示した入力画面です。</returns>
    /// <remarks>要求サイズの上限はパイプラインの保護であり、業務上の容量条件はアップロード条件で検証します。</remarks>
    [HttpPost("execute")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Execute(ExampleInput input, IFormFile? detailFile, CancellationToken ct)
    {
        if (!ModelState.IsValid) return await FailureViewAsync(input, ct);

        ExampleOutcome outcome;
        IActionResult? preparedResult = null;
        try
        {
            await using var content = detailFile?.OpenReadStream();
            outcome = await examples.CalculateAsync(new ExampleRequest(input, content, detailFile?.FileName), ct);
            if (outcome.Result is { } result)
            {
                // 出力の生成が終わるまで成功ログを記録しません。結果ファイルは保存せず、POSTの応答で返します。
                if (input.Output == ExampleOutput.Download)
                    preparedResult = File(await examples.WriteTsvAsync(result, ct), "text/tab-separated-values", examples.BuildFileName(result));
                else
                {
                    ViewData.SetPageShell(shell.Build("計算結果"));
                    preparedResult = View("TemplateExampleResult", result);
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
            ModelState.AddValidationResult(outcome.Errors, nameof(ExampleViewModel.Input), ExampleService.DetailFileField);
            return await FailureViewAsync(input, ct);
        }

        await usage.WriteExecuteAsync(succeeded: true, ct);
        return preparedResult!;
    }

    /// <summary>入力不正を一度だけ記録し、入力内容を保持したまま入力画面へ戻します。</summary>
    /// <param name="input">再表示する入力です。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>項目エラーを表示した入力画面です。</returns>
    private async Task<IActionResult> FailureViewAsync(ExampleInput input, CancellationToken ct)
    {
        await usage.WriteExecuteAsync(succeeded: false, ct);
        return View(ExampleIndexView, await BuildViewAsync(input, InvalidInputMessage, ct));
    }

    /// <summary>入力画面の表示情報を組み立てます。ファイル選択は再表示しません。</summary>
    /// <param name="input">表示する入力です。</param>
    /// <param name="message">画面上部へ表示する案内です。表示しない場合はNULLです。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>入力画面の表示情報です。</returns>
    private async Task<ExampleViewModel> BuildViewAsync(ExampleInput input, OperationMessage? message, CancellationToken ct)
    {
        // パンくずの上位（Portalのトップ・ツール一覧）と対象ツール名はCommonが付けます。
        ViewData.SetPageShell(shell.Build("入力"));
        return new ExampleViewModel
        {
            Input = input,
            Categories = examples.GetCategories(),
            DetailFile = await examples.GetDetailFileHintAsync(ct),
            Message = message
        };
    }
}
