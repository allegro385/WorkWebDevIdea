using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SalesSupport.Portal.Web.Controllers;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>ログインIDの受渡し、資格情報の非表示、戻り先の安全性を確認します。</summary>
public sealed class AccountControllerTests
{
    /// <summary>メール形式ではないログインIDを認証へ渡し、安全な戻り先へ遷移します。</summary>
    [Fact]
    public async Task LoginAcceptsExternalIdAndLocalReturnUrl()
    {
        var service = new StubAccountService { Result = new(LoginOutcome.Succeeded, ReturnUrl: "/tools") };
        var controller = Create(service);
        var result = await controller.Login(new LoginInput { LoginId = "staff001", Password = "secret", ReturnUrl = "/tools" }, default);
        Assert.Equal("staff001", service.LoginId);
        Assert.Equal("/tools", Assert.IsType<LocalRedirectResult>(result).Url);
    }

    /// <summary>失敗時は共通文言で再表示し、パスワードを入力モデルへ残しません。</summary>
    [Fact]
    public async Task RejectedLoginClearsPassword()
    {
        var controller = Create(new StubAccountService());
        var result = await controller.Login(new LoginInput { LoginId = "staff001", Password = "secret" }, default);
        var model = Assert.IsType<LoginViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("staff001", model.Input.LoginId);
        Assert.Null(model.Input.Password);
        Assert.Contains(controller.ModelState.Values.SelectMany(x => x.Errors), x => x.ErrorMessage == "ログインIDまたはパスワードが正しくありません。");
    }

    /// <summary>外部URLを戻り先へ指定してもトップへ遷移します。</summary>
    [Fact]
    public async Task ExternalReturnUrlIsRejected()
    {
        var controller = Create(new StubAccountService { Result = new(LoginOutcome.Succeeded) });
        var result = await controller.Login(new LoginInput { LoginId = "staff001", Password = "secret", ReturnUrl = "https://example.invalid/" }, default);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    /// <summary>Private時は一般ユーザーを入場制限案内へ遷移させます。</summary>
    [Fact]
    public async Task PrivateLoginRedirectsToNotice()
    {
        var controller = Create(new StubAccountService { Result = new(LoginOutcome.Succeeded, true) });
        var result = await controller.Login(new LoginInput { LoginId = "staff001", Password = "secret" }, default);
        Assert.Equal("Private", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    /// <summary>HTTPコンテキスト付きの対象Controllerを生成します。</summary>
    private static AccountController Create(IAccountService service) => new(service, new ConfigurationBuilder().Build())
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    /// <summary>認証に渡されたログインIDと指定結果を保持します。</summary>
    private sealed class StubAccountService : IAccountService
    {
        public LoginResult Result { get; init; } = new(LoginOutcome.Rejected);
        public string? LoginId { get; private set; }

        /// <summary>指定した認証結果を返します。</summary>
        public Task<LoginResult> SignInAsync(string? loginId, string? password, string? returnUrl = null, CancellationToken ct = default)
        {
            LoginId = loginId;
            return Task.FromResult(Result);
        }

        /// <summary>OTP要求に対して指定した結果だけを返します。</summary>
        public Task<LoginResult> VerifyOtpAsync(string? code, bool rememberBrowser, CancellationToken ct = default) => Task.FromResult(Result);
        /// <summary>再送の表示を確認するため指定結果を返します。</summary>
        public Task<LoginResult> ResendOtpAsync(CancellationToken ct = default) => Task.FromResult(Result);
        /// <summary>一時認証済みの画面を表示します。</summary>
        public Task<bool> HasPendingLoginAsync() => Task.FromResult(true);
    }

    /// <summary>パスワード確認後はOTP画面に進み、通常画面へ遷移しません。</summary>
    [Fact]
    public async Task PasswordSuccessDisplaysOtp()
    {
        var controller = Create(new StubAccountService { Result = new(LoginOutcome.RequiresOtp) });
        var result = await controller.Login(new LoginInput { LoginId = "staff001", Password = "secret" }, default);
        Assert.IsType<OtpViewModel>(Assert.IsType<ViewResult>(result).Model);
    }

    /// <summary>コードの再表示を避け、記憶指定だけを保持します。</summary>
    [Fact]
    public async Task OtpFailureClearsCodeAndModelState()
    {
        var controller = Create(new StubAccountService());
        controller.ModelState.SetModelValue("Input.Code", "123456", "123456");
        var result = await controller.Otp(new OtpInput { Code = "123456", RememberBrowser = true }, default);
        var model = Assert.IsType<OtpViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Null(model.Input.Code);
        Assert.True(model.Input.RememberBrowser);
        Assert.Empty(controller.ModelState);
    }

    /// <summary>パスワードの入力値を消しても、必須入力などのエラーは表示できる状態にします。</summary>
    [Fact]
    public async Task PasswordRedisplayKeepsValidationButClearsAttemptedValue()
    {
        var controller = Create(new StubAccountService());
        controller.ModelState.SetModelValue("Input.Password", "secret", "secret");
        controller.ModelState.AddModelError("Input.Password", "パスワードを入力してください。");
        await controller.Login(new LoginInput { LoginId = "staff001", Password = "secret" }, default);
        Assert.Null(controller.ModelState["Input.Password"]!.AttemptedValue);
        Assert.Single(controller.ModelState["Input.Password"]!.Errors);
    }
}
