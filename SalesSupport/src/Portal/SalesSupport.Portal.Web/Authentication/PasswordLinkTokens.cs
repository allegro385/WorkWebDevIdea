using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Entities.Identity;

namespace SalesSupport.Portal.Web.Authentication;

/// <summary>設定リンクの用途です。有効期限と発行番号を用途ごとに分けます。</summary>
public enum PasswordLinkKind
{
    /// <summary>初回パスワード設定用です。</summary>
    Initial,
    /// <summary>パスワード再設定用です。</summary>
    Reset
}

/// <summary>AspNetUserTokensへ保持する設定リンクの記録形式です。</summary>
public static class PasswordLinkTokens
{
    /// <summary>設定リンク用に固定するLoginProviderです。</summary>
    public const string Provider = "SalesSupport.PasswordLinks";
    /// <summary>初回設定用の最新発行番号を保持するNameです。</summary>
    public const string InitialIssue = "InitialIssue";
    /// <summary>再設定用の最新発行番号を保持するNameです。</summary>
    public const string ResetIssue = "ResetIssue";
    /// <summary>再設定請求の最終受付時刻を保持するNameです。</summary>
    public const string LastRequestUtc = "LastRequestUtc";
    /// <summary>初回設定用TokenProviderの登録名です。</summary>
    public const string InitialProviderName = "SalesSupportInitialPassword";
    /// <summary>再設定用TokenProviderの登録名です。</summary>
    public const string ResetProviderName = "SalesSupportResetPassword";
    /// <summary>初回設定でGenerateUserTokenAsyncへ渡す用途名です。</summary>
    public const string InitialPurpose = "SalesSupport:InitialPassword";

    /// <summary>用途に対応する発行番号のNameを返します。</summary>
    public static string IssueName(PasswordLinkKind kind) => kind == PasswordLinkKind.Initial ? InitialIssue : ResetIssue;

    /// <summary>用途に対応するTokenProviderの登録名を返します。</summary>
    public static string ProviderName(PasswordLinkKind kind) => kind == PasswordLinkKind.Initial ? InitialProviderName : ResetProviderName;

    /// <summary>発行番号として受け付ける32桁のN形式かを判定します。</summary>
    public static bool IsIssue(string? value) => value is { Length: 32 } && Guid.TryParseExact(value, "N", out _);

    /// <summary>新しい発行番号を生成します。</summary>
    public static string NewIssue() => Guid.NewGuid().ToString("N");
}

/// <summary>初回設定リンクの有効期限（14日）を保持する設定です。</summary>
public sealed class InitialPasswordTokenProviderOptions : DataProtectionTokenProviderOptions
{
    /// <summary>初回設定専用の保護名と有効期限を設定します。</summary>
    public InitialPasswordTokenProviderOptions()
    {
        Name = "SalesSupport.InitialPassword";
        TokenLifespan = TimeSpan.FromDays(14);
    }
}

/// <summary>再設定リンクの有効期限（1時間）を保持する設定です。</summary>
public sealed class ResetPasswordTokenProviderOptions : DataProtectionTokenProviderOptions
{
    /// <summary>再設定専用の保護名と有効期限を設定します。</summary>
    public ResetPasswordTokenProviderOptions()
    {
        Name = "SalesSupport.ResetPassword";
        TokenLifespan = TimeSpan.FromHours(1);
    }
}

/// <summary>標準の保護・期限検証へ委譲し、用途へDBの最新発行番号だけを加えます。</summary>
public abstract class PasswordLinkTokenProvider : DataProtectorTokenProvider<ApplicationUser>
{
    private readonly string issueName;

    /// <summary>用途別の保護設定と、照合する発行番号のNameを受け取ります。</summary>
    protected PasswordLinkTokenProvider(IDataProtectionProvider dataProtectionProvider, IOptions<DataProtectionTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<ApplicationUser>> logger, string issueName) : base(dataProtectionProvider, options, logger) =>
        this.issueName = issueName;

    /// <summary>最新発行番号を含む用途でトークンを生成します。発行番号がなければ生成しません。</summary>
    public override async Task<string> GenerateAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var composed = await ComposeAsync(purpose, manager, user);
        if (composed is null) throw new InvalidOperationException("設定リンクの発行番号が確定していません。");
        return await base.GenerateAsync(composed, manager, user);
    }

    /// <summary>発行番号が消費・再発行されたトークンを受け付けません。</summary>
    public override async Task<bool> ValidateAsync(string purpose, string token, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var composed = await ComposeAsync(purpose, manager, user);
        return composed is not null && await base.ValidateAsync(composed, token, manager, user);
    }

    /// <summary>現在の発行番号を用途へ連結します。未発行・形式不正はnullを返します。</summary>
    private async Task<string?> ComposeAsync(string purpose, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var issue = await manager.GetAuthenticationTokenAsync(user, PasswordLinkTokens.Provider, issueName);
        return PasswordLinkTokens.IsIssue(issue) ? purpose + ":" + issue : null;
    }
}

/// <summary>初回設定リンク用のTokenProviderです。</summary>
public sealed class InitialPasswordTokenProvider(IDataProtectionProvider dataProtectionProvider,
    IOptions<InitialPasswordTokenProviderOptions> options, ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : PasswordLinkTokenProvider(dataProtectionProvider, options, logger, PasswordLinkTokens.InitialIssue);

/// <summary>再設定リンク用のTokenProviderです。</summary>
public sealed class ResetPasswordTokenProvider(IDataProtectionProvider dataProtectionProvider,
    IOptions<ResetPasswordTokenProviderOptions> options, ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : PasswordLinkTokenProvider(dataProtectionProvider, options, logger, PasswordLinkTokens.ResetIssue);
