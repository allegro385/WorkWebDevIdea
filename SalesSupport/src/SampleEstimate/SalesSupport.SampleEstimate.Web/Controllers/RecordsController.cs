using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Logging;
using SalesSupport.Common.UI;
using SalesSupport.SampleEstimate.Web.Models;
using SalesSupport.SampleEstimate.Web.Services;

namespace SalesSupport.SampleEstimate.Web.Controllers;

/// <summary>本人の保存済み案件を検索・表示・更新します。業務判断はServiceに委譲します。</summary>
[Route("records")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RecordsController(IEstimateRecordService records, IEstimateService estimates, IUsageLogger usage,
    IOptions<CommonOptions> options) : Controller
{
    /// <summary>条件に一致する案件を一覧と合計で表示します。</summary>
    [HttpGet("")]
    [TypeFilter(typeof(WebOpenLoggingFilter))]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, string? categoryCode, CancellationToken ct)
    {
        var categories = estimates.GetCategories();
        if (from > to) ModelState.AddModelError("To", "終了日は開始日以降にしてください。");
        if (!string.IsNullOrWhiteSpace(categoryCode) && !categories.Any(x => x.Code == categoryCode))
            ModelState.AddModelError("CategoryCode", "区分を選択してください。");
        var result = ModelState.IsValid ? await records.ListAsync(from, to, categoryCode, ct) : new EstimateRecordList([], 0, 0);
        ViewData.SetPageShell(Shell("案件一覧・集計"));
        return View(new RecordListViewModel { From = from, To = to, CategoryCode = categoryCode, Categories = categories, Result = result });
    }

    /// <summary>本人の保存済み要約を表示します。入力ファイルやTSVは再取得できません。</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var record = await records.GetAsync(id, ct);
        if (record is null) return NotFound();
        ViewData.SetPageShell(Shell("保存した案件"));
        return View(record);
    }

    /// <summary>本人の案件を編集フォームに読み込みます。</summary>
    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var record = await records.GetAsync(id, ct);
        if (record is null) return NotFound();
        ViewData.SetPageShell(Shell("案件の編集"));
        return View(RecordEditViewModel.FromRecord(record, estimates.GetCategories()));
    }

    /// <summary>再計算した値を更新回数が一致する場合だけ保存し、二重送信は競合として返します。</summary>
    [HttpPost("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, RecordEditViewModel model, CancellationToken ct)
    {
        if (id != model.RecordId)
        {
            await usage.WriteAsync(new UsageEvent(options.Value.ToolId ?? "", "WEB_EXECUTE", "FAILURE"), ct);
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
                        await usage.WriteAsync(new UsageEvent(options.Value.ToolId ?? "", "WEB_EXECUTE", "FAILURE"), ct);
                        return NotFound();
                    }
                    if (update == RecordUpdateResult.Updated)
                    {
                        await usage.WriteAsync(new UsageEvent(options.Value.ToolId ?? "", "WEB_EXECUTE", "SUCCESS"), ct);
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
            await usage.WriteAsync(new UsageEvent(options.Value.ToolId ?? "", "WEB_EXECUTE", "FAILURE"), CancellationToken.None);
            throw;
        }
        await usage.WriteAsync(new UsageEvent(options.Value.ToolId ?? "", "WEB_EXECUTE", "FAILURE"), ct);
        ViewData.SetPageShell(Shell("案件の編集"));
        return View(model);
    }

    /// <summary>共通レイアウトのパンくずを組み立てます。</summary>
    private PageShellModel Shell(string title) => new()
    {
        PageTitle = title,
        CurrentToolName = options.Value.ApplicationName,
        Breadcrumbs =
        [
            new Breadcrumb("トップ", "", LinkTarget.Portal),
            new Breadcrumb("ツール一覧", "tools", LinkTarget.Portal),
            new Breadcrumb("見積試算", "", LinkTarget.Local),
            new Breadcrumb(title)
        ]
    };
}
