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
        var input = new LoginInput { ReturnUrl = CommonValidation.IsLocalPath(returnUrl) ? returnUrl : null };
        return LoginView(input);
    }

    /// <summary>資格情報を検証し、成功時だけ共有Cookieを発行します。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(PortalRateLimits.Login)]
    public async Task<IActionResult> Login([Bind(Prefix = "Input")] LoginInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid) return LoginView(input);
        var result = await accounts.SignInAsync(input.LoginId, input.Password, input.ReturnUrl, ct);
        if (result.Outcome is LoginOutcome.RequiresOtp or LoginOutcome.DeliveryFailed) return OtpView(new(), Message(result.Outcome));
        if (result.Outcome != LoginOutcome.Succeeded)
        {
            // 登録有無・無効状態・パスワード誤りを区別しない共通文言だけを表示します。
            ModelState.AddModelError(string.Empty, result.Outcome == LoginOutcome.Throttled ? Message(result.Outcome)! : "ログインIDまたはパスワードが正しくありません。");
            return LoginView(input);
        }
        return Complete(result);
    }

    /// <summary>送信や保存を行わず、パスワード確認済みの利用者へOTP入力画面を表示します。</summary>
    [HttpGet("otp")]
    [AllowAnonymous]
    public async Task<IActionResult> Otp() => await accounts.HasPendingLoginAsync() ? OtpView(new()) : RedirectToAction(nameof(Login));

    /// <summary>CSRF検証後にコードを一度だけ検証し、成功時に元の画面へ戻します。</summary>
    [HttpPost("otp")]
    [AllowAnonymous]
    [EnableRateLimiting(PortalRateLimits.Login)]
    public async Task<IActionResult> Otp([Bind(Prefix = "Input")] OtpInput input, CancellationToken ct)
    {
        // 形式不正も照合失敗に含め、フォーム検証による回数制限の迂回を防ぎます。
        var result = await accounts.VerifyOtpAsync(input.Code, input.RememberBrowser, ct);
        return result.Outcome == LoginOutcome.Succeeded ? Complete(result) : OtpView(input, Message(result.Outcome));
    }

    /// <summary>本人に束縛された送信枠でのみ再送し、戻り先や利用者をフォームから受け取りません。</summary>
    [HttpPost("otp/resend")]
    [AllowAnonymous]
    [EnableRateLimiting(PortalRateLimits.Login)]
    public async Task<IActionResult> ResendOtp(CancellationToken ct)
    {
        var result = await accounts.ResendOtpAsync(ct);
        return OtpView(new(), result.Outcome == LoginOutcome.RequiresOtp ? "新しい確認コードを送信しました。以前のコードは使用できません。" : Message(result.Outcome));
    }

    /// <summary>コードの入力値とModelStateの秘密値を破棄して表示します。</summary>
    private IActionResult OtpView(OtpInput input, string? message = null)
    {
        input.Code = null;
        ModelState.Clear();
        ViewData.SetPageShell(Shell("ログイン確認"));
        return View("Otp", new OtpViewModel { Input = input, Message = message });
    }

    /// <summary>認証済み結果からだけ、安全な元画面または入場制限案内へ遷移します。</summary>
    private IActionResult Complete(LoginResult result)
    {
        if (result.RequiresPrivateNotice) return RedirectToAction(nameof(HomeController.Private), "Home");
        return CommonValidation.IsLocalPath(result.ReturnUrl) ? LocalRedirect(result.ReturnUrl!) : RedirectToAction(nameof(HomeController.Index), "Home");
    }

    /// <summary>内部情報やメールアドレスを含まない利用者向けの案内です。</summary>
    private static string? Message(LoginOutcome outcome) => outcome switch
    {
        LoginOutcome.RequiresOtp => null,
        LoginOutcome.DeliveryFailed => "確認コードを送信できませんでした。時間をおいて再送するか、社内のシステム担当へお問い合わせください。",
        LoginOutcome.Throttled => "送信回数の上限または再送間隔の制限に達しました。しばらく時間をおいてからお試しください。",
        LoginOutcome.Expired => "確認コードまたはログイン手続きが無効です。再送するか、ログインからやり直してください。",
        _ => "確認コードを確認できませんでした。入力を確認するか、時間をおいてログインからやり直してください。"
    };

    /// <summary>ログイン画面を組み立てます。入力されたパスワードは復元しません。</summary>
    private IActionResult LoginView(LoginInput input)
    {
        input.Password = null;
        if (ModelState.TryGetValue("Input.Password", out var passwordState))
        {
            passwordState.RawValue = null;
            passwordState.AttemptedValue = null;
        }
        ViewData.SetPageShell(Shell("ログイン"));
        return View("Login", new LoginViewModel { Input = input, SupportContact = CommonValidation.Normalize(configuration["Portal:SupportContact"]) });
    }

    /// <summary>認証画面では通常機能への共通メニューを表示しません。</summary>
    private static PageShellModel Shell(string title) => new() { PageTitle = title, ShowCommonMenus = false };
}
