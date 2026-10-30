using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Logging;
using SalesSupport.Common.UI;
using SalesSupport.SampleEstimate.Web.Models;
using SalesSupport.SampleEstimate.Web.Services;

namespace SalesSupport.SampleEstimate.Web.Controllers;

/// <summary>本人の保存済み案件を検索・表示・更新します。業務判断はServiceに委譲します。</summary>
[Route("records")]
[Authorize(Policy = "SalesSupportTool")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RecordsController(IEstimateRecordService records, IEstimateService estimates, IToolUsageLogger usage,
    IToolPageShell shell, IOptions<CommonOptions> options) : Controller
{
    /// <summary>ツール内の上位階層です。パンくずの上位（Portalのトップ・ツール一覧）はCommonが付けます。</summary>
    private static readonly Breadcrumb ToolTop = new("見積試算", "", LinkTarget.Local);

    /// <summary>本人を特定できない構成（単独開発・匿名モード）では、この画面の全要求を対象なしとして拒否します。</summary>
    /// <param name="context">実行しようとしているアクションの情報です。</param>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!options.Value.HasIdentifiedUser) context.Result = NotFound();
        base.OnActionExecuting(context);
    }

    /// <summary>条件に一致する案件を一覧と合計で表示します。</summary>
    /// <remarks>ツール起動の記録は入口画面（ToolController）だけで行い、ツール内の画面遷移では記録しません。</remarks>
    [HttpGet("")]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, string? categoryCode, CancellationToken ct)
    {
        var categories = estimates.GetCategories();
        if (from > to) ModelState.AddModelError("To", "終了日は開始日以降にしてください。");
        if (!string.IsNullOrWhiteSpace(categoryCode) && !categories.Any(x => x.Code == categoryCode))
            ModelState.AddModelError("CategoryCode", "区分を選択してください。");
        var result = ModelState.IsValid ? await records.ListAsync(from, to, categoryCode, ct) : new EstimateRecordList([], 0, 0);
        ViewData.SetPageShell(shell.Build("案件一覧・集計", ToolTop));
        return View(new RecordListViewModel { From = from, To = to, CategoryCode = categoryCode, Categories = categories, Result = result });
    }

    /// <summary>本人の保存済み要約を表示します。入力ファイルやTSVは再取得できません。</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var record = await records.GetAsync(id, ct);
        if (record is null) return NotFound();
        ViewData.SetPageShell(shell.Build("保存した案件", ToolTop));
        return View(record);
    }

    /// <summary>本人の案件を編集フォームに読み込みます。</summary>
    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var record = await records.GetAsync(id, ct);
        if (record is null) return NotFound();
        ViewData.SetPageShell(shell.Build("案件の編集", ToolTop));
        return View(RecordEditViewModel.FromRecord(record, estimates.GetCategories()));
    }

    /// <summary>再計算した値を更新回数が一致する場合だけ保存し、二重送信は競合として返します。</summary>
    [HttpPost("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, RecordEditViewModel model, CancellationToken ct)
    {
        if (id != model.RecordId)
        {
            await usage.WriteExecuteAsync(succeeded: false, ct);
            return NotFound();
        }
        model.Categories = estimates.GetCategories();
        model.Input.Output = EstimateOutput.Screen;
        try
        {
            if (ModelState.IsValid)
            {
                var outcome = await estimates.CalculateAsync(new EstimateRequest(model.Input, null, null), ct);
                if (outcome.Result is { } result)
                {
                    var update = await records.UpdateAsync(id, model.UpdateCount, model.Input, result, ct);
                    if (update == RecordUpdateResult.NotFound)
                    {
                        await usage.WriteExecuteAsync(succeeded: false, ct);
                        return NotFound();
                    }
                    if (update == RecordUpdateResult.Updated)
                    {
                        await usage.WriteExecuteAsync(succeeded: true, ct);
                        return RedirectToAction(nameof(Details), new { id });
                    }
                    ModelState.AddModelError("", "ほかの更新が先に保存されました。案件を開き直してから編集してください。");
                }
                else
                    foreach (var error in outcome.Errors.Errors)
                        ModelState.AddModelError("Input." + error.Field, error.Message);
            }
        }
        catch
        {
            await usage.WriteExecuteAsync(succeeded: false, CancellationToken.None);
            throw;
        }
        await usage.WriteExecuteAsync(succeeded: false, ct);
        ViewData.SetPageShell(shell.Build("案件の編集", ToolTop));
        return View(model);
    }
}
