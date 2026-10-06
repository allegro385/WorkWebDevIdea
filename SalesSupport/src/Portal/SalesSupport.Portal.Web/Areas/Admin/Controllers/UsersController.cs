using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.MasterData;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Areas.Admin.Models;
using SalesSupport.Portal.Web.Bootstrap;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Areas.Admin.Controllers;

/// <summary>A002 ユーザー管理です。検索・一般ロールと有効状態の編集・ロック解除を扱います。</summary>
/// <remarks>一般ロール間の変更だけを許可し、ユーザーの物理削除は行いません。</remarks>
[Area("Admin")]
[Route("admin/users")]
[Authorize(Policy = PortalPolicies.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsersController(IUserAdminService users, ICurrentUserAccessor current) : Controller
{
    /// <summary>検索条件に一致するユーザーを一覧表示します。</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] UserSearchInput search, CancellationToken ct)
    {
        ViewData.SetPageShell(Shell("ユーザー管理"));
        return View(new UserListViewModel
        {
            Search = search,
            Users = await users.SearchAsync(search, ct),
            Roles = await users.GetRolesAsync(true, ct),
            Message = PortalMessages.Take(TempData)
        });
    }

    /// <summary>ユーザー編集画面を表示します。</summary>
    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> Edit(Guid userId, CancellationToken ct)
    {
        var model = await users.GetAsync(userId, ct);
        if (model is null) return NotFound();
        model.Message = PortalMessages.Take(TempData);
        ViewData.SetPageShell(Shell("ユーザー編集"));
        return View(model);
    }

    /// <summary>有効状態と一般ロールを更新します。</summary>
    [HttpPost("{userId:guid}")]
    public async Task<IActionResult> Update(Guid userId, UserEditInput input, CancellationToken ct)
    {
        if (current.User is not { } operatorUser) return Unauthorized();
        input.UserId = userId;
        if (!ModelState.IsValid) return await EditViewAsync(userId, input, null, ct);

        var result = await users.UpdateAsync(operatorUser.UserId, input, ct);
        if (result.Outcome == UserAdminOutcome.Saved)
        {
            PortalMessages.Set(TempData, OperationMessageKind.Success, "ユーザー情報を保存しました。");
            return RedirectToAction(nameof(Edit), new { userId });
        }
        ModelState.AddValidationResult(result.Errors);
        return await EditViewAsync(userId, input, MessageFor(result.Outcome, "ユーザー情報を保存できませんでした。"), ct);
    }

    /// <summary>ロックを解除します。有効状態と権限は変更しません。</summary>
    [HttpPost("{userId:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid userId, string? concurrencyStamp, CancellationToken ct)
    {
        var result = await users.UnlockAsync(userId, concurrencyStamp, ct);
        PortalMessages.Set(TempData, result.Outcome == UserAdminOutcome.Saved ? OperationMessageKind.Success : OperationMessageKind.Conflict,
            result.Outcome == UserAdminOutcome.Saved ? "ロックを解除しました。" : "他の操作で更新されています。再読み込みして最新の内容を確認してください。");
        return RedirectToAction(nameof(Edit), new { userId });
    }

    /// <summary>編集画面を入力を保持したまま再表示します。</summary>
    private async Task<IActionResult> EditViewAsync(Guid userId, UserEditInput input, OperationMessage? message, CancellationToken ct)
    {
        var model = await users.GetAsync(userId, ct);
        if (model is null) return NotFound();
        model.Input = input;
        model.Message = message;
        ViewData.SetPageShell(Shell("ユーザー編集"));
        return View("Edit", model);
    }

    /// <summary>保存できなかった場合の画面内メッセージを作成します。</summary>
    private static OperationMessage MessageFor(UserAdminOutcome outcome, string failureText) => outcome == UserAdminOutcome.Conflict
        ? new(OperationMessageKind.Conflict, "他の操作で更新されています。再読み込みして最新の内容を確認してください。")
        : new(OperationMessageKind.Failure, failureText);

    /// <summary>管理画面の共通の表示情報を返します。</summary>
    private static PageShellModel Shell(string title) => new()
    {
        PageTitle = title,
        Breadcrumbs = title == "ユーザー管理"
            ? [new Breadcrumb("トップ", ""), new Breadcrumb(title)]
            : [new Breadcrumb("トップ", ""), new Breadcrumb("ユーザー管理", "admin/users"), new Breadcrumb(title)]
    };
}
