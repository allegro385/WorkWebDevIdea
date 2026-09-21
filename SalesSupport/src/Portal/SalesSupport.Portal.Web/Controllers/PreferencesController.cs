using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Controllers;

/// <summary>P006 個人設定です。本人の通知設定だけを表示・更新します。</summary>
[Route("preferences")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PreferencesController(IPreferenceService preferences, ICurrentUserAccessor current) : Controller
{
    /// <summary>本人の通知設定を表示します。</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (current.User is not { } user) return Unauthorized();
        var preference = await preferences.GetAsync(user.UserId, ct);
        if (preference is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        return PreferenceView(new PreferenceInput
        {
            SystemNoticeMailEnabled = preference.SystemNoticeMailEnabled,
            FavoriteToolNoticeMailEnabled = preference.FavoriteToolNoticeMailEnabled,
            UpdateCount = preference.UpdateCount
        }, PortalMessages.Take(TempData));
    }

    /// <summary>入力された2項目を保存し、結果を操作後のメッセージとして表示します。</summary>
    [HttpPost("")]
    public async Task<IActionResult> Index(PreferenceInput input, CancellationToken ct)
    {
        if (current.User is not { } user) return Unauthorized();
        if (!ModelState.IsValid) return PreferenceView(input, null);

        var result = await preferences.SaveAsync(user.UserId,
            new UserPreferencesDto(input.SystemNoticeMailEnabled, input.FavoriteToolNoticeMailEnabled, input.UpdateCount), ct);
        switch (result.Outcome)
        {
            case PreferenceOutcome.Succeeded:
                PortalMessages.Set(TempData, OperationMessageKind.Success, "通知設定を保存しました。");
                return RedirectToAction(nameof(Index));
            case PreferenceOutcome.Conflict:
                return PreferenceView(input, new OperationMessage(OperationMessageKind.Conflict,
                    "他の操作で更新されています。再読み込みして最新の内容を確認してください。"));
            default:
                return PreferenceView(input, new OperationMessage(OperationMessageKind.Failure,
                    "通知設定を保存できませんでした。管理者へお問い合わせください。"));
        }
    }

    /// <summary>個人設定画面を組み立てます。</summary>
    private IActionResult PreferenceView(PreferenceInput input, OperationMessage? message)
    {
        ViewData.SetPageShell(new PageShellModel
        {
            PageTitle = "個人設定",
            Breadcrumbs = [new Breadcrumb("トップ", ""), new Breadcrumb("個人設定")]
        });
        return View("Index", new PreferenceViewModel { Input = input, Message = message });
    }
}
