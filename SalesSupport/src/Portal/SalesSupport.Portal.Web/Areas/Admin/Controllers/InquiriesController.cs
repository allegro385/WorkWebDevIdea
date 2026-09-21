using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.MasterData;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Areas.Admin.Models;
using SalesSupport.Portal.Web.Bootstrap;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Areas.Admin.Controllers;

/// <summary>A005 問い合わせ管理です。検索と対応状況の更新を行います。</summary>
/// <remarks>送信者と本文は変更できません。添付ファイルは保存していないため表示しません。</remarks>
[Area("Admin")]
[Route("admin/inquiries")]
[Authorize(Policy = PortalPolicies.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InquiriesController(IInquiryAdminService inquiries, ICodeMasterReader codes) : Controller
{
    /// <summary>検索条件に一致する問い合わせを一覧表示します。</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] InquirySearchInput search, CancellationToken ct)
    {
        ViewData.SetPageShell(Shell("問い合わせ管理", null));
        return View(await BuildListAsync(search, PortalMessages.Take(TempData), ct));
    }

    /// <summary>一覧で変更した行を一括保存します。1件でも失敗した場合は全件を保存しません。</summary>
    [HttpPost("")]
    public async Task<IActionResult> Save([Bind(Prefix = "Search")] InquirySearchInput search,
        [Bind(Prefix = "Input")] InquiryEditInput input, CancellationToken ct)
    {
        var result = await inquiries.SaveAsync(input.Rows, ct);
        if (result.Outcome == InquiryAdminOutcome.Saved)
        {
            PortalMessages.Set(TempData, OperationMessageKind.Success, "変更内容を保存しました。");
            return RedirectToAction(nameof(Index), new
            {
                search.InquiryId,
                search.From,
                search.To,
                search.CategoryCode,
                search.Target,
                search.AssigneeUserId,
                search.Status,
                search.AdminNote,
                search.Content
            });
        }
        ModelState.AddValidationResult(result.Errors);
        ViewData.SetPageShell(Shell("問い合わせ管理", null));
        return View("Index", await BuildListAsync(search, MessageFor(result.Outcome), ct));
    }

    /// <summary>問い合わせ詳細を表示します。本文は省略せず表示します。</summary>
    [HttpGet("{inquiryId}")]
    public async Task<IActionResult> Detail(string inquiryId, CancellationToken ct)
    {
        var inquiry = await inquiries.GetAsync(inquiryId, ct);
        if (inquiry is not { } found) return NotFound();
        ViewData.SetPageShell(Shell("問い合わせ詳細", inquiryId));
        return View(await BuildDetailAsync(found.Detail, found.Input, PortalMessages.Take(TempData), ct));
    }

    /// <summary>詳細画面で変更した内容を保存します。</summary>
    [HttpPost("{inquiryId}")]
    public async Task<IActionResult> Update(string inquiryId, InquiryEditRow input, CancellationToken ct)
    {
        input.InquiryId = inquiryId;
        var result = await inquiries.SaveAsync([input], ct);
        if (result.Outcome == InquiryAdminOutcome.Saved)
        {
            PortalMessages.Set(TempData, OperationMessageKind.Success, "変更内容を保存しました。");
            return RedirectToAction(nameof(Detail), new { inquiryId });
        }

        var inquiry = await inquiries.GetAsync(inquiryId, ct);
        if (inquiry is not { } found) return NotFound();
        ModelState.AddValidationResult(result.Errors);
        ViewData.SetPageShell(Shell("問い合わせ詳細", inquiryId));
        return View("Detail", await BuildDetailAsync(found.Detail, input, MessageFor(result.Outcome), ct));
    }

    /// <summary>一覧画面の表示情報を組み立てます。</summary>
    private async Task<InquiryListViewModel> BuildListAsync(InquirySearchInput search, OperationMessage? message, CancellationToken ct) => new()
    {
        Search = search,
        Inquiries = await inquiries.SearchAsync(search, ct),
        Categories = await codes.GetOptionsAsync("INQUIRY_CATEGORY", ct),
        Statuses = await codes.GetOptionsAsync("INQUIRY_STATUS", ct),
        Targets = await inquiries.GetTargetsAsync(ct),
        Assignees = await inquiries.GetAssigneesAsync(ct),
        Message = message
    };

    /// <summary>詳細画面の表示情報を組み立てます。</summary>
    private async Task<InquiryDetailViewModel> BuildDetailAsync(InquiryDetailView detail, InquiryEditRow input,
        OperationMessage? message, CancellationToken ct) => new()
        {
            Detail = detail,
            Input = input,
            Categories = await codes.GetOptionsAsync("INQUIRY_CATEGORY", ct),
            Statuses = await codes.GetOptionsAsync("INQUIRY_STATUS", ct),
            Targets = await inquiries.GetTargetsAsync(ct),
            Assignees = await inquiries.GetAssigneesAsync(ct),
            Message = message
        };

    /// <summary>保存できなかった場合の画面内メッセージを作成します。</summary>
    private static OperationMessage MessageFor(InquiryAdminOutcome outcome) => outcome == InquiryAdminOutcome.Conflict
        ? new(OperationMessageKind.Conflict, "他の操作で更新されています。再読み込みして最新の内容を確認してください。")
        : new(OperationMessageKind.Failure, "入力内容を確認してください。変更は保存していません。");

    /// <summary>管理画面の共通の表示情報を返します。</summary>
    private static PageShellModel Shell(string title, string? inquiryId) => new()
    {
        PageTitle = title,
        Breadcrumbs = inquiryId is null
            ? [new Breadcrumb("トップ", ""), new Breadcrumb(title)]
            : [new Breadcrumb("トップ", ""), new Breadcrumb("問い合わせ管理", "admin/inquiries"), new Breadcrumb(title)]
    };
}
