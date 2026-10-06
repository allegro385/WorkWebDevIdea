using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SalesSupport.Common.UI;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Bootstrap;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Controllers;

/// <summary>外部連携された資格情報によるログインを扱います。</summary>
[Route("account")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController(IAccountService accounts, IConfiguration configuration) : Controller
{
    /// <summary>ログイン画面を表示します。戻り先はサイト内の経路だけを保持します。</summary>
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl)
    {
        var input = new LoginInput { ReturnUrl = CommonValidation.IsLocalReturnUrl(returnUrl) ? returnUrl : null };
        return LoginView(input);
    }

    /// <summary>資格情報を検証し、成功時だけ共有Cookieを発行します。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(PortalRateLimits.Login)]
    public async Task<IActionResult> Login([Bind(Prefix = "Input")] LoginInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return LoginView(input);
        var result = await accounts.SignInAsync(input.LoginId, input.Password, ct);
        if (result.Outcome != LoginOutcome.Succeeded)
        {
            // 登録有無・無効状態・パスワード誤りを区別しない共通文言だけを表示します。
            ModelState.AddModelError(string.Empty, "ログインIDまたはパスワードが正しくありません。");
            return LoginView(input);
        }
        if (result.RequiresPrivateNotice) return RedirectToAction(nameof(HomeController.Private), "Home");
        return CommonValidation.IsLocalReturnUrl(input.ReturnUrl) ? LocalRedirect(input.ReturnUrl!) : RedirectToAction(nameof(HomeController.Index), "Home");
    }

    /// <summary>ログイン画面を組み立てます。入力されたパスワードは復元しません。</summary>
    private IActionResult LoginView(LoginInput input)
    {
        input.Password = null;
        ViewData.SetPageShell(Shell("ログイン"));
        return View("Login", new LoginViewModel { Input = input, SupportContact = CommonValidation.Normalize(configuration["Portal:SupportContact"]) });
    }

    /// <summary>認証画面では通常機能への共通メニューを表示しません。</summary>
    private static PageShellModel Shell(string title) => new() { PageTitle = title, ShowCommonMenus = false };
}
