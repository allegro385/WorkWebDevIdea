using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Controllers;

/// <summary>P005 利用マニュアル・FAQです。マニュアルとFAQを一つの画面に表示します。</summary>
[Route("help")]
public sealed class HelpController(IHelpService help) : Controller
{
    /// <summary>マニュアルへの導線と公開中のFAQを表示します。</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData.SetPageShell(new PageShellModel
        {
            PageTitle = "利用マニュアル・FAQ",
            Breadcrumbs = [new Breadcrumb("トップ", ""), new Breadcrumb("利用マニュアル・FAQ")]
        });
        return View(new HelpViewModel(await help.GetFaqAsync(ct)));
    }

    /// <summary>管理された配置のマニュアルPDFを認可済みの経路から返します。</summary>
    [HttpGet("manual")]
    public async Task<IActionResult> Manual(CancellationToken ct)
    {
        var content = await help.OpenManualAsync(ct);
        if (content is null) return NotFound();
        return File(content, "application/pdf", help.ManualFileName);
    }
}
