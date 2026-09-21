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

/// <summary>A002 ユーザー管理です。検索・編集・ロック解除とTSV一括登録を扱います。</summary>
/// <remarks>権限の変更機能は画面・管理APIともに設けません。ユーザーの物理削除も行いません。</remarks>
[Area("Admin")]
[Route("admin/users")]
[Authorize(Policy = PortalPolicies.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsersController(IUserAdminService users, IUserImportService imports, IPasswordLinkService links,
    ICodeMasterReader codes, ICurrentUserAccessor current) : Controller
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
            Roles = await codes.GetOptionsAsync("USER_ROLE", ct),
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

    /// <summary>表示名・メールアドレス・有効状態を更新します。権限は変更しません。</summary>
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

    /// <summary>設定リンクを新しく発行して案内します。送信失敗を理由とする再送は行いません。</summary>
    [HttpPost("{userId:guid}/password-link")]
    public async Task<IActionResult> SendPasswordLink(Guid userId, CancellationToken ct)
    {
        var issued = await links.IssueForUserAsync(userId, ct);
        PortalMessages.Set(TempData, issued ? OperationMessageKind.Success : OperationMessageKind.Warning,
            issued ? "設定用メールの送信処理が終了しました。" : "対象のユーザーへは設定リンクを発行できませんでした。");
        return RedirectToAction(nameof(Edit), new { userId });
    }

    /// <summary>TSV取込の選択画面を表示します。</summary>
    [HttpGet("import")]
    public IActionResult Import()
    {
        ViewData.SetPageShell(Shell("新規登録（TSV取り込み）"));
        return View(new UserImportViewModel { Message = PortalMessages.Take(TempData) });
    }

    /// <summary>TSVを検証し、エラーがなければ登録内容の確認を表示します。登録は行いません。</summary>
    [HttpPost("import")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Validate(IFormFile? file, CancellationToken ct)
    {
        if (current.User is not { } operatorUser) return Unauthorized();
        ViewData.SetPageShell(Shell("新規登録（TSV取り込み）"));
        if (file is null || file.Length == 0)
            return View("Import", new UserImportViewModel
            {
                Message = new OperationMessage(OperationMessageKind.Failure, "取り込むTSVファイルを選択してください。")
            });

        await using var content = file.OpenReadStream();
        var preview = await imports.ValidateAsync(operatorUser.UserId, content, file.FileName, ct);
        return View("Import", new UserImportViewModel
        {
            Rows = preview.Rows,
            Errors = preview.Errors,
            ConfirmationId = preview.ConfirmationId,
            Message = preview.Errors.Count == 0
                ? new OperationMessage(OperationMessageKind.Warning, $"{preview.Rows.Count}件を登録します。内容を確認してください。")
                : new OperationMessage(OperationMessageKind.Failure, "エラーがあるため登録を開始しません。内容を修正して取り込み直してください。")
        });
    }

    /// <summary>確認IDを一度だけ使用して登録します。結果は行番号とともに表示します。</summary>
    [HttpPost("import/execute")]
    public async Task<IActionResult> Execute(Guid confirmationId, CancellationToken ct)
    {
        if (current.User is not { } operatorUser) return Unauthorized();
        var results = await imports.ExecuteAsync(operatorUser.UserId, confirmationId, ct);
        ViewData.SetPageShell(Shell("新規登録（TSV取り込み）"));
        return View("Import", new UserImportViewModel
        {
            Results = results,
            Message = results.Count == 0
                ? new OperationMessage(OperationMessageKind.Failure, "確認内容を利用できませんでした。TSVを取り込み直してください。")
                : new OperationMessage(OperationMessageKind.Success, "登録処理が終了しました。")
        });
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
