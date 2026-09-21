using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Models;

namespace SalesSupport.Portal.Web.Controllers;

/// <summary>ポータルトップと入場制限案内を表示します。</summary>
public sealed class HomeController(ISystemSettingsReader settings) : Controller
{
    /// <summary>ポータルトップを表示します。お知らせ・メニューの実装は後続の参照画面で追加します。</summary>
    public IActionResult Index()
    {
        ViewData.SetPageShell(new PageShellModel { PageTitle = "ポータルトップ" });
        return View();
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
