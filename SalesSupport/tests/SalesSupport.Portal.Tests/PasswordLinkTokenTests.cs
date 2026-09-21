using Microsoft.AspNetCore.Identity;
using SalesSupport.Portal.Web.Authentication;
using SalesSupport.Portal.Web.Models;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>設定リンクの記録形式と用途別の振り分けを確認します。</summary>
public sealed class PasswordLinkTokenTests
{
    /// <summary>GuidのN形式32桁だけを発行番号として受け付けることを確認します。</summary>
    [Fact]
    public void IsIssueAcceptsOnlyThirtyTwoDigitFormat()
    {
        Assert.True(PasswordLinkTokens.IsIssue(PasswordLinkTokens.NewIssue()));
        Assert.False(PasswordLinkTokens.IsIssue(Guid.NewGuid().ToString("D")));
        Assert.False(PasswordLinkTokens.IsIssue(Guid.NewGuid().ToString("N")[..31]));
        Assert.False(PasswordLinkTokens.IsIssue(""));
        Assert.False(PasswordLinkTokens.IsIssue(null));
    }

    /// <summary>生成した発行番号が毎回異なることを確認します。</summary>
    [Fact]
    public void NewIssueReturnsDistinctValues()
    {
        Assert.NotEqual(PasswordLinkTokens.NewIssue(), PasswordLinkTokens.NewIssue());
    }

    /// <summary>用途ごとに別の発行番号とTokenProviderを使うことを確認します。</summary>
    [Fact]
    public void KindSelectsSeparateIssueAndProvider()
    {
        Assert.Equal(PasswordLinkTokens.InitialIssue, PasswordLinkTokens.IssueName(PasswordLinkKind.Initial));
        Assert.Equal(PasswordLinkTokens.ResetIssue, PasswordLinkTokens.IssueName(PasswordLinkKind.Reset));
        Assert.NotEqual(PasswordLinkTokens.ProviderName(PasswordLinkKind.Initial), PasswordLinkTokens.ProviderName(PasswordLinkKind.Reset));
    }

    /// <summary>初回と再設定の有効期限を14日・1時間に分けることを確認します。</summary>
    [Fact]
    public void ProviderOptionsSeparateLifespans()
    {
        Assert.Equal(TimeSpan.FromDays(14), new InitialPasswordTokenProviderOptions().TokenLifespan);
        Assert.Equal(TimeSpan.FromHours(1), new ResetPasswordTokenProviderOptions().TokenLifespan);
        Assert.NotEqual(new InitialPasswordTokenProviderOptions().Name, new ResetPasswordTokenProviderOptions().Name);
    }

    /// <summary>画面の見出しと送信先が用途に対応することを確認します。</summary>
    [Fact]
    public void LinkInputSelectsViewTargetsByKind()
    {
        Assert.Equal("初回パスワード設定", new PasswordLinkInput { Kind = PasswordLinkKind.Initial }.Title);
        Assert.Equal("PasswordSetup", new PasswordLinkInput { Kind = PasswordLinkKind.Initial }.ActionName);
        Assert.Equal("パスワード再設定", new PasswordLinkInput { Kind = PasswordLinkKind.Reset }.Title);
        Assert.Equal("PasswordReset", new PasswordLinkInput { Kind = PasswordLinkKind.Reset }.ActionName);
    }

    /// <summary>Identity API経由の登録・再設定にも同じパスワード条件が適用されることを確認します。</summary>
    [Fact]
    public async Task PasswordValidatorAppliesPortalPolicy()
    {
        var validator = new PortalPasswordValidator(PasswordPolicy.FromEntries(["Portal-Test#2026"]));
        Assert.True((await validator.ValidateAsync(null!, null!, "Another-Test#2026")).Succeeded);
        var rejected = await validator.ValidateAsync(null!, null!, "Portal-Test#2026");
        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Errors, error => error.Code == "FORBIDDEN");
    }

    /// <summary>Identityの既定文言ではなく、日本語の項目エラーを返すことを確認します。</summary>
    [Fact]
    public async Task PasswordValidatorReturnsJapaneseMessages()
    {
        var validator = new PortalPasswordValidator(PasswordPolicy.FromEntries([]));
        IdentityResult result = await validator.ValidateAsync(null!, null!, "short");
        Assert.All(result.Errors, error => Assert.Contains("ください。", error.Description));
    }
}
