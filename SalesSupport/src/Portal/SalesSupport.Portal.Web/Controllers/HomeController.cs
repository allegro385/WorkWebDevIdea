using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Controllers;

/// <summary>ポータルトップと入場制限案内を表示します。</summary>
public sealed class HomeController(ISystemSettingsReader settings, ISystemNoticeService notices, ICurrentUserAccessor current, IPreferenceService preferences) : Controller
{
    /// <summary>P002 ポータルトップです。公開中のシステムお知らせと主要機能の入口を表示します。</summary>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>トップ画面です。匿名モードの共通ユーザーには本人向けの入口を表示しない指定で返します。</returns>
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData.SetPageShell(new PageShellModel { PageTitle = "トップ", UseTitleBand = true });
        var published = await notices.GetPublishedAsync(ct);
        // 共通ユーザーはAspNetUsersに存在しないため、本人の通知設定を確認しません。
        var identified = current.HasIdentifiedUser();
        return View(new HomeViewModel(published, identified && current.User?.RoleCode == "ADMIN",
            identified && await preferences.IsAllowedAsync(current.User!.UserId, ct), identified));
    }

    /// <summary>Private公開時の案内画面です。公開状態の表示名称や利用者情報は表示しません。</summary>
    [HttpGet("/private")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Private(CancellationToken ct)
    {
        ViewData.SetPageShell(new PageShellModel { PageTitle = "ご利用案内", ShowCommonMenus = false });
        return View(new PrivateNoticeViewModel(await settings.GetPrivateMessageAsync(ct)));
    }
}
