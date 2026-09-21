using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Authentication;
using SalesSupport.Portal.Web.Controllers;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>認証フォームの成功時リダイレクトと設定リンク検証順序を確認します。</summary>
public sealed class AccountControllerTests
{
    /// <summary>再設定請求成功後に受付画面へリダイレクトすることを確認します。</summary>
    [Fact]
    public async Task PasswordRequestRedirectsAfterSuccess()
    {
        var links = new StubPasswordLinkService();
        var controller = CreateController(links: links);

        var result = await controller.PasswordRequest(new PasswordRequestInput { Email = "user@example.com" }, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.PasswordRequestAccepted), redirect.ActionName);
        Assert.Equal(1, links.RequestCount);
    }

    /// <summary>パスワード変更成功後に完了画面へリダイレクトすることを確認します。</summary>
    [Fact]
    public async Task PasswordChangeRedirectsAfterSuccess()
    {
        var account = new StubAccountService { ChangeResult = PasswordChangeResult.From(PasswordChangeOutcome.Succeeded) };
        var controller = CreateController(account, current: new StubCurrentUserAccessor
        {
            User = new CurrentUser(Guid.NewGuid(), "利用者", "USER")
        });

        var result = await controller.PasswordChange(new PasswordChangeInput
        {
            CurrentPassword = "CurrentPassword!123",
            NewPassword = "NewPassword!1234",
            NewPasswordConfirmation = "NewPassword!1234"
        }, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.PasswordChanged), redirect.ActionName);
    }

    /// <summary>入力不一致より先にリンクを検証し、無効リンクを共通案内にすることを確認します。</summary>
    [Fact]
    public async Task PasswordResetRejectsInvalidLinkBeforeMismatchError()
    {
        var links = new StubPasswordLinkService { IsValid = false };
        var controller = CreateController(links: links);

        var result = await controller.PasswordReset(new PasswordLinkInput
        {
            User = Guid.NewGuid(),
            Token = "invalid",
            Password = "NewPassword!1234",
            PasswordConfirmation = "DifferentPassword!1234"
        }, default);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("PasswordLinkInvalid", view.ViewName);
        Assert.Equal(1, links.ValidateCount);
        Assert.Equal(0, links.ConsumeCount);
    }

    /// <summary>有効な再設定リンクの消費成功後に完了画面へリダイレクトすることを確認します。</summary>
    [Fact]
    public async Task PasswordResetRedirectsAfterSuccess()
    {
        var links = new StubPasswordLinkService
        {
            IsValid = true,
            ConsumeResult = PasswordLinkResult.From(PasswordLinkOutcome.Succeeded)
        };
        var controller = CreateController(links: links);

        var result = await controller.PasswordReset(new PasswordLinkInput
        {
            User = Guid.NewGuid(),
            Token = "valid",
            Password = "NewPassword!1234",
            PasswordConfirmation = "NewPassword!1234"
        }, default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.PasswordResetCompleted), redirect.ActionName);
        Assert.Equal(1, links.ValidateCount);
        Assert.Equal(1, links.ConsumeCount);
    }

    /// <summary>テスト対象をHTTP応答ヘッダーへアクセスできる状態で生成します。</summary>
    private static AccountController CreateController(IAccountService? account = null, IPasswordLinkService? links = null,
        ICurrentUserAccessor? current = null)
    {
        var controller = new AccountController(account ?? new StubAccountService(), links ?? new StubPasswordLinkService(),
            current ?? new StubCurrentUserAccessor(), new ConfigurationBuilder().Build());
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    /// <summary>Controllerテストで必要な認証結果だけを返します。</summary>
    private sealed class StubAccountService : IAccountService
    {
        public PasswordChangeResult ChangeResult { get; init; } = PasswordChangeResult.From(PasswordChangeOutcome.Unavailable);

        /// <summary>ログインを拒否する既定結果を返します。</summary>
        public Task<LoginResult> SignInAsync(string? email, string? password, CancellationToken ct = default) =>
            Task.FromResult(new LoginResult(LoginOutcome.Rejected));

        /// <summary>テストで指定したパスワード変更結果を返します。</summary>
        public Task<PasswordChangeResult> ChangePasswordAsync(Guid userId, string? currentPassword, string? newPassword,
            CancellationToken ct = default) => Task.FromResult(ChangeResult);
    }

    /// <summary>リンク検証・消費の呼出し回数と指定結果を保持します。</summary>
    private sealed class StubPasswordLinkService : IPasswordLinkService
    {
        public bool IsValid { get; init; }
        public PasswordLinkResult ConsumeResult { get; init; } = PasswordLinkResult.From(PasswordLinkOutcome.InvalidLink);
        public int RequestCount { get; private set; }
        public int ValidateCount { get; private set; }
        public int ConsumeCount { get; private set; }

        public int IssueCount { get; private set; }

        /// <summary>再設定請求の呼出し回数を記録します。</summary>
        public Task RequestAsync(string? email, CancellationToken ct = default)
        {
            RequestCount++;
            return Task.CompletedTask;
        }

        /// <summary>管理者による再発行の呼出し回数を記録します。</summary>
        public Task<bool> IssueForUserAsync(Guid userId, CancellationToken ct = default)
        {
            IssueCount++;
            return Task.FromResult(true);
        }

        /// <summary>リンク検証の呼出し回数を記録して指定結果を返します。</summary>
        public Task<bool> ValidateAsync(Guid userId, string? token, PasswordLinkKind kind, CancellationToken ct = default)
        {
            ValidateCount++;
            return Task.FromResult(IsValid);
        }

        /// <summary>リンク消費の呼出し回数を記録して指定結果を返します。</summary>
        public Task<PasswordLinkResult> ConsumeAsync(Guid userId, string? token, PasswordLinkKind kind, string? password,
            CancellationToken ct = default)
        {
            ConsumeCount++;
            return Task.FromResult(ConsumeResult);
        }
    }

    /// <summary>テストで指定した検証済み利用者を返します。</summary>
    private sealed class StubCurrentUserAccessor : ICurrentUserAccessor
    {
        public CurrentUser? User { get; init; }
    }
}
