using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Areas.Admin.Models;
using SalesSupport.Portal.Web.Bootstrap;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Areas.Admin.Controllers;

/// <summary>A006 サイト管理です。システムのお知らせとツールのお知らせの保存・送信を扱います。</summary>
/// <remarks>サイト公開状態の表示・変更欄は設けません。公開状態の切替はDB運用で行います。</remarks>
[Area("Admin")]
[Route("admin/notices")]
[Authorize(Policy = PortalPolicies.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NoticesController(INoticeService notices, ToolEditPageBuilder pages, ICurrentUserAccessor current,
    IAccessEvaluator access, PortalDbContext db) : Controller
{
    /// <summary>システムのお知らせの一覧と編集欄を表示します。</summary>
    /// <param name="notice">編集欄へ読み込むお知らせです。未指定では編集欄を表示しません。</param>
    /// <param name="create">新規登録の空欄を表示するかどうかです。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    [HttpGet("")]
    public async Task<IActionResult> Index(int? notice, bool create, CancellationToken ct)
    {
        var editor = await ResolveEditorAsync("SYSTEM", null, notice, create, ct);
        return SiteView(await BuildSectionAsync("SYSTEM", null, editor, PortalMessages.Take(TempData), ct));
    }

    /// <summary>システムのお知らせを保存します。メール送信は行いません。</summary>
    [HttpPost("save")]
    public Task<IActionResult> Save(NoticeEditInput input, CancellationToken ct) => SaveAsync("SYSTEM", null, input, ct);

    /// <summary>ツールのお知らせを保存します。対象ツールの管理権限を確認します。</summary>
    [HttpPost("tools/{toolId}/save")]
    public async Task<IActionResult> SaveToolNotice(string toolId, NoticeEditInput input, CancellationToken ct)
    {
        if (await DenyToolAsync(toolId, ct) is { } denied) return denied;
        return await SaveAsync("TOOL", toolId, input, ct);
    }

    /// <summary>選択したシステムのお知らせについて、送信件数と宛先人数の確認を表示します。</summary>
    [HttpPost("send/confirm")]
    public Task<IActionResult> ConfirmSend(int[] noticeIds, CancellationToken ct) => ConfirmSendAsync("SYSTEM", null, noticeIds, ct);

    /// <summary>選択したツールのお知らせについて、送信件数と宛先人数の確認を表示します。</summary>
    [HttpPost("tools/{toolId}/send/confirm")]
    public async Task<IActionResult> ConfirmToolSend(string toolId, int[] noticeIds, CancellationToken ct)
    {
        if (await DenyToolAsync(toolId, ct) is { } denied) return denied;
        return await ConfirmSendAsync("TOOL", toolId, noticeIds, ct);
    }

    /// <summary>確認済みのシステムのお知らせを送信します。</summary>
    [HttpPost("send")]
    public Task<IActionResult> Send(Guid confirmationId, CancellationToken ct) => SendAsync("SYSTEM", null, confirmationId, ct);

    /// <summary>確認済みのツールのお知らせを送信します。</summary>
    [HttpPost("tools/{toolId}/send")]
    public async Task<IActionResult> SendToolNotice(string toolId, Guid confirmationId, CancellationToken ct)
    {
        if (await DenyToolAsync(toolId, ct) is { } denied) return denied;
        return await SendAsync("TOOL", toolId, confirmationId, ct);
    }

    /// <summary>保存結果を操作後のメッセージとして表示し、対象の画面へ戻します。</summary>
    private async Task<IActionResult> SaveAsync(string noticeType, string? toolId, NoticeEditInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return await RedisplayAsync(noticeType, toolId, input, null, ct);

        var result = await notices.SaveAsync(noticeType, toolId,
            new NoticeEditModel(input.NoticeId, input.Title, input.Content, input.IsPublished, input.UpdateCount), ct);
        switch (result.Outcome)
        {
            case NoticeSaveOutcome.Saved:
                PortalMessages.Set(TempData, OperationMessageKind.Success, "お知らせを保存しました。");
                return RedirectToSection(noticeType, toolId);
            case NoticeSaveOutcome.InvalidInput:
                ModelState.AddValidationResult(result.Errors);
                return await RedisplayAsync(noticeType, toolId, input, null, ct);
            case NoticeSaveOutcome.Conflict:
                return await RedisplayAsync(noticeType, toolId, input,
                    new OperationMessage(OperationMessageKind.Conflict, "他の操作で更新されています。再読み込みして最新の内容を確認してください。"), ct);
            default:
                return await RedisplayAsync(noticeType, toolId, input,
                    new OperationMessage(OperationMessageKind.Failure, "お知らせを保存できませんでした。"), ct);
        }
    }

    /// <summary>選択件数と宛先人数を確定し、確認画面を表示します。</summary>
    private async Task<IActionResult> ConfirmSendAsync(string noticeType, string? toolId, int[] noticeIds, CancellationToken ct)
    {
        if (current.User is not { } user) return Unauthorized();
        var plan = await notices.PrepareSendAsync(user.UserId, noticeType, toolId, noticeIds, ct);
        if (plan is null)
        {
            PortalMessages.Set(TempData, OperationMessageKind.Warning, "送信するお知らせを選択してください。");
            return RedirectToSection(noticeType, toolId);
        }

        var toolName = toolId is null ? null : await db.Tools.AsNoTracking().Where(x => x.ToolId == toolId)
            .Select(x => x.ToolName).SingleOrDefaultAsync(ct);
        ViewData.SetPageShell(Shell("お知らせの送信確認", toolId, toolName));
        return View("SendConfirm", new NoticeSendConfirmViewModel(noticeType, toolId, toolName,
            plan.ConfirmationId, plan.NoticeCount, plan.RecipientCount));
    }

    /// <summary>確認IDを一度だけ使用して送信し、結果を操作後のメッセージとして表示します。</summary>
    private async Task<IActionResult> SendAsync(string noticeType, string? toolId, Guid confirmationId, CancellationToken ct)
    {
        if (current.User is not { } user) return Unauthorized();
        var outcome = await notices.SendAsync(user.UserId, confirmationId, ct);
        var message = outcome switch
        {
            // 配達成功や失敗を断定せず、処理が終了したことだけを表示します。
            NoticeSendOutcome.Sent => new OperationMessage(OperationMessageKind.Success, "送信処理が終了しました。"),
            NoticeSendOutcome.Conflict => new OperationMessage(OperationMessageKind.Conflict,
                "確認時と内容または宛先が変わっています。もう一度確認してください。"),
            NoticeSendOutcome.NoRecipients => new OperationMessage(OperationMessageKind.Warning, "送信対象者がいないため送信しませんでした。"),
            _ => new OperationMessage(OperationMessageKind.Failure, "送信処理が終了しました。")
        };
        PortalMessages.Set(TempData, message.Kind, message.Text);
        return RedirectToSection(noticeType, toolId);
    }

    /// <summary>入力を保持したままお知らせ区画を再表示します。</summary>
    private async Task<IActionResult> RedisplayAsync(string noticeType, string? toolId, NoticeEditInput input, OperationMessage? message, CancellationToken ct)
    {
        if (noticeType != "TOOL" || toolId is null)
            return SiteView(await BuildSectionAsync(noticeType, null, input, message, ct));

        // ツールのお知らせは編集画面の一区画のため、保存できない場合も同じツール編集画面へ表示を戻します。
        var model = await pages.BuildAsync(toolId, ct, noticeEditor: input, noticeMessage: message);
        if (model is null) return NotFound();
        ViewData.SetPageShell(Shell("ツール編集", toolId, model.ToolName));
        return View("~/Areas/Admin/Views/Tools/Edit.cshtml", model);
    }

    /// <summary>お知らせ区画の表示情報を組み立てます。</summary>
    private async Task<NoticeSectionViewModel> BuildSectionAsync(string noticeType, string? toolId, NoticeEditInput? editor,
        OperationMessage? message, CancellationToken ct) => new()
        {
            NoticeType = noticeType,
            ToolId = toolId,
            Notices = await notices.GetListAsync(noticeType, toolId, ct),
            Editor = editor,
            Message = message
        };

    /// <summary>編集欄へ読み込む入力を決定します。新規登録の初期公開状態は非公開です。</summary>
    private async Task<NoticeEditInput?> ResolveEditorAsync(string noticeType, string? toolId, int? noticeId, bool create, CancellationToken ct)
    {
        if (create) return new NoticeEditInput { IsPublished = false };
        if (noticeId is not { } id) return null;
        var notice = await notices.GetAsync(id, noticeType, toolId, ct);
        if (notice is null) return null;
        return new NoticeEditInput
        {
            NoticeId = notice.NoticeId,
            Title = notice.Title,
            Content = notice.Content,
            IsPublished = notice.IsPublished,
            UpdateCount = notice.UpdateCount
        };
    }

    /// <summary>対象ツールの管理権限を確認し、許可されない場合の応答を返します。</summary>
    private async Task<IActionResult?> DenyToolAsync(string toolId, CancellationToken ct)
    {
        var decision = await access.EvaluateAsync(new AccessRequest(AccessPurpose.ToolManage, toolId), ct);
        return decision.Allowed ? null : StatusCode(decision.StatusCode);
    }

    /// <summary>保存・送信後に戻る画面を決定します。</summary>
    private IActionResult RedirectToSection(string noticeType, string? toolId) => noticeType == "TOOL"
        ? RedirectToAction("Edit", "Tools", new { area = "Admin", toolId })
        : RedirectToAction(nameof(Index));

    /// <summary>サイト管理のお知らせ画面を組み立てます。</summary>
    private IActionResult SiteView(NoticeSectionViewModel section)
    {
        ViewData.SetPageShell(Shell("サイト管理", null, null));
        return View("Index", section);
    }

    /// <summary>管理画面の共通の表示情報を返します。</summary>
    private static PageShellModel Shell(string title, string? toolId, string? toolName) => new()
    {
        PageTitle = title,
        CurrentToolName = toolId is null ? null : toolName,
        Breadcrumbs = [new Breadcrumb("トップ", ""), new Breadcrumb(title)]
    };
}
