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

/// <summary>A001 ツール管理です。ツール選択と、区画ごとに独立した保存を提供します。</summary>
/// <remarks>新規ツール登録画面は設けません。新規登録は所定のSQL等の手動運用で行います。</remarks>
[Area("Admin")]
[Route("admin/tools")]
[Authorize(Policy = PortalPolicies.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ToolsController(IToolAdminService tools, IToolFileAdminService files, INoticeService notices,
    ToolEditPageBuilder pages, ICodeMasterReader codes, ICurrentUserAccessor current, IAccessEvaluator access) : Controller
{
    /// <summary>絞り込み条件に一致するツールを一覧表示します。非公開のツールも対象です。</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ToolFilterInput filter, CancellationToken ct)
    {
        ViewData.SetPageShell(Shell("ツール管理", null));
        return View(new ToolSelectionViewModel
        {
            Filter = filter,
            Tools = await tools.GetSelectionAsync(filter, ct),
            Categories = await tools.GetCategoriesAsync(ct),
            Owners = await tools.GetOwnersAsync(ct),
            Statuses = await codes.GetOptionsAsync("TOOL_STATUS", ct),
            Message = PortalMessages.Take(TempData)
        });
    }

    /// <summary>一覧で並べ替えた表示順を一括保存します。競合時は保存しません。</summary>
    [HttpPost("order")]
    public async Task<IActionResult> SaveOrder(ToolOrderInput input, CancellationToken ct)
    {
        var result = await tools.SaveOrderAsync(input.Rows, ct);
        PortalMessages.Set(TempData, MessageKindOf(result.Outcome), MessageOf(result, "表示順を保存しました。"));
        return RedirectToAction(nameof(Index));
    }

    /// <summary>ツール編集画面を表示します。お知らせ、バージョン、基本情報、提供内容を同じ画面に並べます。</summary>
    /// <param name="toolId">編集対象のツールです。</param>
    /// <param name="notice">お知らせ編集欄へ読み込む対象です。</param>
    /// <param name="createNotice">お知らせを新規登録する空欄を表示するかどうかです。</param>
    /// <param name="version">バージョン編集欄へ読み込む対象です。</param>
    /// <param name="createVersion">バージョンを新規登録する空欄を表示するかどうかです。</param>
    /// <param name="reference">参考資料編集欄へ読み込む対象です。</param>
    /// <param name="createReference">参考資料を追加する空欄を表示するかどうかです。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    [HttpGet("{toolId}")]
    public async Task<IActionResult> Edit(string toolId, int? notice, bool createNotice, int? version, bool createVersion,
        int? reference, bool createReference, CancellationToken ct)
    {
        if (await DenyAsync(toolId, ct) is { } denied) return denied;
        var versionEditor = createVersion ? new ToolVersionInput { ReleasedAt = null } : version is { } historyId ? await tools.GetVersionEditorAsync(toolId, historyId, ct) : null;
        var referenceEditor = createReference ? new ToolReferenceInput() : reference is { } fileId ? await files.GetReferenceEditorAsync(toolId, fileId, ct) : null;
        var noticeEditor = createNotice ? new NoticeEditInput { IsPublished = false } : await LoadNoticeEditorAsync(toolId, notice, ct);
        return await EditViewAsync(toolId, ct, versionEditor: versionEditor, referenceEditor: referenceEditor, noticeEditor: noticeEditor,
            message: PortalMessages.Take(TempData));
    }

    /// <summary>基本情報を保存します。ほかの区画の項目は更新しません。</summary>
    [HttpPost("{toolId}/basic")]
    public async Task<IActionResult> SaveBasic(string toolId, ToolBasicInput input, CancellationToken ct)
    {
        if (await DenyAsync(toolId, ct) is { } denied) return denied;
        if (!ModelState.IsValid) return await EditViewAsync(toolId, ct, basic: input);

        var result = await tools.SaveBasicAsync(toolId, input, ct);
        if (result.Outcome == ToolAdminOutcome.Saved) return SavedRedirect(toolId, "基本情報を保存しました。");
        ModelState.AddValidationResult(result.Errors);
        return await EditViewAsync(toolId, ct, basic: input, basicMessage: FailureMessage(result.Outcome));
    }

    /// <summary>バージョン履歴を追加します。現在のバージョンは変更しません。</summary>
    [HttpPost("{toolId}/versions/create")]
    public Task<IActionResult> CreateVersion(string toolId, ToolVersionInput input, CancellationToken ct) =>
        SaveVersionAsync(toolId, input, create: true, ct);

    /// <summary>バージョン履歴を更新します。現在のバージョンは変更しません。</summary>
    [HttpPost("{toolId}/versions/update")]
    public Task<IActionResult> UpdateVersion(string toolId, ToolVersionInput input, CancellationToken ct) =>
        SaveVersionAsync(toolId, input, create: false, ct);

    /// <summary>削除条件を満たすバージョン履歴を削除します。</summary>
    [HttpPost("{toolId}/versions/delete")]
    public async Task<IActionResult> DeleteVersion(string toolId, int toolHistoryId, int updateCount, CancellationToken ct)
    {
        if (await DenyAsync(toolId, ct) is { } denied) return denied;
        var result = await tools.DeleteVersionAsync(toolId, toolHistoryId, updateCount, ct);
        if (result.Outcome == ToolAdminOutcome.Saved) return SavedRedirect(toolId, "バージョン履歴を削除しました。");
        return await EditViewAsync(toolId, ct, versionMessage: MessageFor(result, "バージョン履歴を削除できませんでした。"));
    }

    /// <summary>現在のバージョンを切り替えます。ほかの未保存入力は保持しません。</summary>
    [HttpPost("{toolId}/versions/current")]
    public async Task<IActionResult> SetCurrentVersion(string toolId, ToolCurrentVersionInput input, CancellationToken ct)
    {
        if (await DenyAsync(toolId, ct) is { } denied) return denied;
        var result = await tools.SetCurrentVersionAsync(toolId, input, ct);
        if (result.Outcome == ToolAdminOutcome.Saved) return SavedRedirect(toolId, "現在のバージョンを保存しました。");
        return await EditViewAsync(toolId, ct, versionMessage: MessageFor(result, "現在のバージョンを保存できませんでした。"));
    }

    /// <summary>WebツールのページURLを保存します。</summary>
    [HttpPost("{toolId}/content/url")]
    public async Task<IActionResult> SaveWebUrl(string toolId, string? webAppUrl, int toolUpdateCount, CancellationToken ct)
    {
        if (await DenyAsync(toolId, ct) is { } denied) return denied;
        var result = await files.SaveWebUrlAsync(toolId, webAppUrl, toolUpdateCount, ct);
        if (result.Outcome == ToolAdminOutcome.Saved) return SavedRedirect(toolId, "ページURLを保存しました。");
        ModelState.AddValidationResult(result.Errors);
        return await EditViewAsync(toolId, ct, contentMessage: MessageFor(result, "ページURLを保存できませんでした。"));
    }

    /// <summary>配布アプリを登録または差し替えます。差し替え後は現在のバージョンの確認を案内します。</summary>
    [HttpPost("{toolId}/files/app")]
    [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
    [RequestSizeLimit(524_288_000)]
    public async Task<IActionResult> SaveApp(string toolId, IFormFile? appFile, CancellationToken ct)
    {
        if (await DenyAsync(toolId, ct) is { } denied) return denied;
        if (current.User is not { } user) return Unauthorized();

        await using var content = appFile?.OpenReadStream();
        var result = await files.SaveAppAsync(toolId, user.UserId, content, appFile?.FileName, ct);
        if (result.Outcome == ToolAdminOutcome.Saved) return SavedRedirect(toolId, "配布アプリを保存しました。現在のバージョンも確認してください。");
        ModelState.AddValidationResult(result.Errors);
        return await EditViewAsync(toolId, ct, contentMessage: MessageFor(result, "配布アプリを保存できませんでした。"));
    }

    /// <summary>参考資料を追加、または表示名・ファイルを更新します。</summary>
    [HttpPost("{toolId}/files/reference")]
    [RequestFormLimits(MultipartBodyLengthLimit = 104_857_600)]
    [RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> SaveReference(string toolId, ToolReferenceInput input, IFormFile? referenceFile, CancellationToken ct)
    {
        if (await DenyAsync(toolId, ct) is { } denied) return denied;
        if (current.User is not { } user) return Unauthorized();
        if (!ModelState.IsValid) return await EditViewAsync(toolId, ct, referenceEditor: input);

        await using var content = referenceFile?.OpenReadStream();
        var result = await files.SaveReferenceAsync(toolId, user.UserId, input, content, referenceFile?.FileName, ct);
        if (result.Outcome == ToolAdminOutcome.Saved) return SavedRedirect(toolId, "参考資料を保存しました。");
        ModelState.AddValidationResult(result.Errors);
        return await EditViewAsync(toolId, ct, referenceEditor: input, contentMessage: MessageFor(result, "参考資料を保存できませんでした。"));
    }

    /// <summary>参考資料を削除します。DBの参照を削除してから実ファイルを削除します。</summary>
    [HttpPost("{toolId}/files/delete")]
    public async Task<IActionResult> DeleteReference(string toolId, int fileId, int updateCount, CancellationToken ct)
    {
        if (await DenyAsync(toolId, ct) is { } denied) return denied;
        var result = await files.DeleteReferenceAsync(toolId, fileId, updateCount, ct);
        if (result.Outcome == ToolAdminOutcome.Saved) return SavedRedirect(toolId, "参考資料を削除しました。");
        return await EditViewAsync(toolId, ct, contentMessage: MessageFor(result, "参考資料を削除できませんでした。"));
    }

    /// <summary>参考資料の表示順を一括保存します。</summary>
    [HttpPost("{toolId}/files/order")]
    public async Task<IActionResult> SaveReferenceOrder(string toolId, ToolFileOrderInput input, CancellationToken ct)
    {
        if (await DenyAsync(toolId, ct) is { } denied) return denied;
        var result = await files.SaveReferenceOrderAsync(toolId, input.Rows, ct);
        if (result.Outcome == ToolAdminOutcome.Saved) return SavedRedirect(toolId, "参考資料の表示順を保存しました。");
        ModelState.AddValidationResult(result.Errors);
        return await EditViewAsync(toolId, ct, contentMessage: MessageFor(result, "表示順を保存できませんでした。"));
    }

    /// <summary>ツール編集画面を組み立てます。指定した区画だけ入力とメッセージを差し替えます。</summary>
    private async Task<IActionResult> EditViewAsync(string toolId, CancellationToken ct,
        ToolBasicInput? basic = null, OperationMessage? basicMessage = null,
        ToolVersionInput? versionEditor = null, OperationMessage? versionMessage = null,
        ToolReferenceInput? referenceEditor = null, OperationMessage? contentMessage = null,
        NoticeEditInput? noticeEditor = null, OperationMessage? noticeMessage = null,
        OperationMessage? message = null)
    {
        var model = await pages.BuildAsync(toolId, ct, basic, basicMessage, versionEditor, versionMessage,
            referenceEditor, contentMessage, noticeEditor, noticeMessage ?? message);
        if (model is null) return NotFound();
        ViewData.SetPageShell(Shell("ツール編集", model.ToolName));
        return View("Edit", model);
    }

    /// <summary>バージョン履歴の追加・更新を保存し、失敗時は入力を保持して再表示します。</summary>
    private async Task<IActionResult> SaveVersionAsync(string toolId, ToolVersionInput input, bool create, CancellationToken ct)
    {
        if (await DenyAsync(toolId, ct) is { } denied) return denied;
        if (!ModelState.IsValid) return await EditViewAsync(toolId, ct, versionEditor: input);

        var result = create ? await tools.CreateVersionAsync(toolId, input, ct) : await tools.UpdateVersionAsync(toolId, input, ct);
        if (result.Outcome == ToolAdminOutcome.Saved) return SavedRedirect(toolId, "バージョン更新情報を保存しました。");
        ModelState.AddValidationResult(result.Errors);
        return await EditViewAsync(toolId, ct, versionEditor: input, versionMessage: FailureMessage(result.Outcome));
    }

    /// <summary>お知らせ編集欄へ読み込む入力を取得します。</summary>
    private async Task<NoticeEditInput?> LoadNoticeEditorAsync(string toolId, int? noticeId, CancellationToken ct)
    {
        if (noticeId is not { } id) return null;
        var notice = await notices.GetAsync(id, "TOOL", toolId, ct);
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
    private async Task<IActionResult?> DenyAsync(string toolId, CancellationToken ct)
    {
        var decision = await access.EvaluateAsync(new AccessRequest(AccessPurpose.ToolManage, toolId), ct);
        return decision.Allowed ? null : StatusCode(decision.StatusCode);
    }

    /// <summary>保存成功後に編集画面へ戻ります。</summary>
    private IActionResult SavedRedirect(string toolId, string text)
    {
        PortalMessages.Set(TempData, OperationMessageKind.Success, text);
        return RedirectToAction(nameof(Edit), new { toolId });
    }

    /// <summary>保存できなかった場合の区画内メッセージを作成します。</summary>
    private static OperationMessage MessageFor(ToolAdminResult result, string failureText) => result.Outcome switch
    {
        ToolAdminOutcome.Conflict => new(OperationMessageKind.Conflict, "他の操作で更新されています。再読み込みして最新の内容を確認してください。"),
        ToolAdminOutcome.InvalidInput => new(OperationMessageKind.Failure, failureText),
        _ => new(OperationMessageKind.Failure, failureText)
    };

    /// <summary>保存できなかった場合の区画内メッセージを判定結果から作成します。</summary>
    private static OperationMessage FailureMessage(ToolAdminOutcome outcome) => outcome == ToolAdminOutcome.Conflict
        ? new(OperationMessageKind.Conflict, "他の操作で更新されています。再読み込みして最新の内容を確認してください。")
        : new(OperationMessageKind.Failure, "入力内容を確認してください。");

    /// <summary>一覧側のメッセージ区分を判定結果から決定します。</summary>
    private static OperationMessageKind MessageKindOf(ToolAdminOutcome outcome) => outcome switch
    {
        ToolAdminOutcome.Saved => OperationMessageKind.Success,
        ToolAdminOutcome.Conflict => OperationMessageKind.Conflict,
        _ => OperationMessageKind.Failure
    };

    /// <summary>一覧側のメッセージ文言を判定結果から決定します。</summary>
    private static string MessageOf(ToolAdminResult result, string successText) => result.Outcome switch
    {
        ToolAdminOutcome.Saved => successText,
        ToolAdminOutcome.Conflict => "他の操作で更新されています。再読み込みして最新の内容を確認してください。",
        ToolAdminOutcome.InvalidInput => result.Errors.Errors.FirstOrDefault()?.Message ?? "入力内容を確認してください。",
        _ => "保存できませんでした。"
    };

    /// <summary>管理画面の共通の表示情報を返します。</summary>
    private static PageShellModel Shell(string title, string? toolName) => new()
    {
        PageTitle = title,
        CurrentToolName = toolName,
        Breadcrumbs = toolName is null
            ? [new Breadcrumb("トップ", ""), new Breadcrumb(title)]
            : [new Breadcrumb("トップ", ""), new Breadcrumb("ツール管理", "admin/tools"), new Breadcrumb(title)]
    };
}
