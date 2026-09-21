using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.UI;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Authentication;
using SalesSupport.Portal.Web.Bootstrap;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Controllers;

/// <summary>ログイン、パスワードの初回設定・再設定・変更を扱います。</summary>
[Route("account")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController(IAccountService accounts, IPasswordLinkService links, ICurrentUserAccessor current,
    IConfiguration configuration) : Controller
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
        var result = await accounts.SignInAsync(input.Email, input.Password, ct);
        if (result.Outcome != LoginOutcome.Succeeded)
        {
            // 登録有無・無効状態・パスワード誤りを区別しない共通文言だけを表示します。
            ModelState.AddModelError(string.Empty, "メールアドレスまたはパスワードが正しくありません。");
            return LoginView(input);
        }
        if (result.RequiresPrivateNotice) return RedirectToAction(nameof(HomeController.Private), "Home");
        return CommonValidation.IsLocalReturnUrl(input.ReturnUrl) ? LocalRedirect(input.ReturnUrl!) : RedirectToAction(nameof(HomeController.Index), "Home");
    }

    /// <summary>再設定メールの請求画面を表示します。</summary>
    [HttpGet("password/request")]
    [AllowAnonymous]
    public IActionResult PasswordRequest() => RequestView(new PasswordRequestInput());

    /// <summary>請求を受け付けます。対象の有無・状態を応答から判別できません。</summary>
    [HttpPost("password/request")]
    [AllowAnonymous]
    [EnableRateLimiting(PortalRateLimits.PasswordRequest)]
    public async Task<IActionResult> PasswordRequest(PasswordRequestInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return RequestView(input);
        await links.RequestAsync(input.Email, ct);
        ViewData.SetPageShell(Shell("パスワード再設定の請求"));
        return View("PasswordRequestAccepted");
    }

    /// <summary>初回パスワード設定画面を表示します。GETでは発行番号を消費しません。</summary>
    [HttpGet("password/setup")]
    [AllowAnonymous]
    public Task<IActionResult> PasswordSetup(Guid user, string? token, CancellationToken ct) =>
        ShowLinkAsync(PasswordLinkKind.Initial, user, token, ct);

    /// <summary>初回パスワードを確定します。</summary>
    [HttpPost("password/setup")]
    [AllowAnonymous]
    public Task<IActionResult> PasswordSetup(PasswordLinkInput input, CancellationToken ct) =>
        ConsumeLinkAsync(PasswordLinkKind.Initial, input, ct);

    /// <summary>パスワード再設定画面を表示します。GETでは発行番号を消費しません。</summary>
    [HttpGet("password/reset")]
    [AllowAnonymous]
    public Task<IActionResult> PasswordReset(Guid user, string? token, CancellationToken ct) =>
        ShowLinkAsync(PasswordLinkKind.Reset, user, token, ct);

    /// <summary>新しいパスワードを確定します。</summary>
    [HttpPost("password/reset")]
    [AllowAnonymous]
    public Task<IActionResult> PasswordReset(PasswordLinkInput input, CancellationToken ct) =>
        ConsumeLinkAsync(PasswordLinkKind.Reset, input, ct);

    /// <summary>ログイン中の利用者向けにパスワード変更画面を表示します。</summary>
    [HttpGet("password/change")]
    public IActionResult PasswordChange() => ChangeView(new PasswordChangeInput());

    /// <summary>現在のパスワードを確認して変更し、既存の認証を終了します。</summary>
    [HttpPost("password/change")]
    public async Task<IActionResult> PasswordChange(PasswordChangeInput input, CancellationToken ct)
    {
        if (current.User is not { } user) return Unauthorized();
        if (!ModelState.IsValid) return ChangeView(input);
        if (input.NewPassword != input.NewPasswordConfirmation)
        {
            ModelState.AddModelError(nameof(input.NewPasswordConfirmation), "新しいパスワードと確認用パスワードが一致しません。");
            return ChangeView(input);
        }
        var result = await accounts.ChangePasswordAsync(user.UserId, input.CurrentPassword, input.NewPassword, ct);
        switch (result.Outcome)
        {
            case PasswordChangeOutcome.Succeeded:
                ViewData.SetPageShell(Shell("パスワード変更"));
                return View("PasswordChanged");
            case PasswordChangeOutcome.InvalidInput:
                ModelState.AddValidationResult(result.Errors);
                return ChangeView(input);
            case PasswordChangeOutcome.InvalidCurrent:
                ModelState.AddModelError(nameof(input.CurrentPassword), "現在のパスワードが正しくありません。");
                return ChangeView(input);
            case PasswordChangeOutcome.Conflict:
                ModelState.AddModelError(string.Empty, "ほかの操作と重なったため保存できませんでした。もう一度実行してください。");
                return ChangeView(input);
            default:
                ModelState.AddModelError(string.Empty, "パスワードを変更できませんでした。管理者へお問い合わせください。");
                return ChangeView(input);
        }
    }

    /// <summary>リンクの有効性だけを確認し、無効なら共通の案内へ切り替えます。</summary>
    private async Task<IActionResult> ShowLinkAsync(PasswordLinkKind kind, Guid user, string? token, CancellationToken ct)
    {
        SuppressReferrer();
        if (!await links.ValidateAsync(user, token, kind, ct)) return InvalidLinkView();
        return LinkView(new PasswordLinkInput { Kind = kind, User = user, Token = token });
    }

    /// <summary>入力を検証してから設定リンクを消費します。入力不正では再入力できます。</summary>
    private async Task<IActionResult> ConsumeLinkAsync(PasswordLinkKind kind, PasswordLinkInput input, CancellationToken ct)
    {
        SuppressReferrer();
        // 用途は経路から決定し、フォームの値で切り替えられないようにします。
        input.Kind = kind;
        if (input.Password != input.PasswordConfirmation)
        {
            ModelState.AddModelError(nameof(input.PasswordConfirmation), "新しいパスワードと確認用パスワードが一致しません。");
            return LinkView(input);
        }
        var result = await links.ConsumeAsync(input.User, input.Token, kind, input.Password, ct);
        switch (result.Outcome)
        {
            case PasswordLinkOutcome.Succeeded:
                ViewData.SetPageShell(Shell(input.Title));
                return View("PasswordLinkCompleted", input);
            case PasswordLinkOutcome.InvalidInput:
                ModelState.AddValidationResult(result.Errors);
                return LinkView(input);
            case PasswordLinkOutcome.Conflict:
                ModelState.AddModelError(string.Empty, "ほかの操作と重なったため保存できませんでした。もう一度実行してください。");
                return LinkView(input);
            default:
                return InvalidLinkView();
        }
    }

    /// <summary>ログイン画面を組み立てます。入力されたパスワードは復元しません。</summary>
    private IActionResult LoginView(LoginInput input)
    {
        input.Password = null;
        ViewData.SetPageShell(Shell("ログイン"));
        return View("Login", new LoginViewModel { Input = input, SupportContact = CommonValidation.Normalize(configuration["Portal:SupportContact"]) });
    }

    /// <summary>請求画面を組み立てます。</summary>
    private IActionResult RequestView(PasswordRequestInput input)
    {
        ViewData.SetPageShell(Shell("パスワード再設定の請求"));
        return View("PasswordRequest", input);
    }

    /// <summary>設定・再設定画面を組み立てます。入力されたパスワードは復元しません。</summary>
    private IActionResult LinkView(PasswordLinkInput input)
    {
        input.Password = null;
        input.PasswordConfirmation = null;
        ViewData.SetPageShell(Shell(input.Title));
        return View("PasswordLink", input);
    }

    /// <summary>変更画面を組み立てます。入力されたパスワードは復元しません。</summary>
    private IActionResult ChangeView(PasswordChangeInput input)
    {
        input.CurrentPassword = null;
        input.NewPassword = null;
        input.NewPasswordConfirmation = null;
        ViewData.SetPageShell(PortalShell("パスワード変更"));
        return View("PasswordChange", input);
    }

    /// <summary>期限切れ・改変・消費済みを区別しない共通の案内を表示します。</summary>
    private IActionResult InvalidLinkView()
    {
        ViewData.SetPageShell(Shell("設定リンク"));
        return View("PasswordLinkInvalid");
    }

    /// <summary>認証画面では通常機能への共通メニューを表示しません。</summary>
    private static PageShellModel Shell(string title) => new() { PageTitle = title, ShowCommonMenus = false };

    /// <summary>ログイン中に開く画面では、共通メニューとトップへ戻る経路を残します。</summary>
    private static PageShellModel PortalShell(string title) =>
        new() { PageTitle = title, Breadcrumbs = [new Breadcrumb("トップ", ""), new Breadcrumb(title)] };

    /// <summary>トークンを含むURLから第三者へRefererを送信させません。</summary>
    private void SuppressReferrer() => Response.Headers.Append("Referrer-Policy", "no-referrer");
}
