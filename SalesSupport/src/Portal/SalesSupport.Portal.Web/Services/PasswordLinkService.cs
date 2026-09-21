using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Mail;
using SalesSupport.Common.UI;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Authentication;
using SalesSupport.Portal.Web.Data;

namespace SalesSupport.Portal.Web.Services;

/// <summary>設定リンクの消費結果です。</summary>
public enum PasswordLinkOutcome
{
    /// <summary>パスワードを確定しました。</summary>
    Succeeded,
    /// <summary>期限切れ・改変・消費済み・対象不正のリンクです。</summary>
    InvalidLink,
    /// <summary>入力が条件を満たしません。発行番号は消費していません。</summary>
    InvalidInput,
    /// <summary>並行更新と競合しました。</summary>
    Conflict
}

/// <summary>消費結果と、入力不正時の項目エラーです。</summary>
public sealed record PasswordLinkResult(PasswordLinkOutcome Outcome, ValidationResult Errors)
{
    /// <summary>項目エラーを伴わない結果を生成します。</summary>
    public static PasswordLinkResult From(PasswordLinkOutcome outcome) => new(outcome, new([]));
}

/// <summary>初回設定・再設定リンクの発行と消費を扱います。</summary>
public interface IPasswordLinkService
{
    /// <summary>再設定請求を受け付けます。結果は対象の有無にかかわらず区別できません。</summary>
    Task RequestAsync(string? email, CancellationToken ct = default);
    /// <summary>GETでの表示可否だけを判定します。発行番号は消費しません。</summary>
    Task<bool> ValidateAsync(Guid userId, string? token, PasswordLinkKind kind, CancellationToken ct = default);
    /// <summary>パスワード確定と発行番号の削除を同一トランザクションで行います。</summary>
    Task<PasswordLinkResult> ConsumeAsync(Guid userId, string? token, PasswordLinkKind kind, string? password, CancellationToken ct = default);
}

/// <summary>ユーザー行の直列化とIdentity APIで、リンクの発行・消費を一度だけ成立させます。</summary>
public sealed class PasswordLinkService(PortalDbContext db, UserManager<ApplicationUser> users, IPasswordPolicy policy,
    IMailSender mail, IMailTemplateRenderer templates, ISalesSupportLinks links, IApplicationClock clock) : IPasswordLinkService
{
    /// <summary>同一アカウントの再設定請求を抑止する間隔です。</summary>
    private static readonly TimeSpan RequestInterval = TimeSpan.FromMinutes(5);

    /// <summary>受付時刻と発行番号を確定してからSMTPを一度だけ呼びます。送信失敗で番号を戻しません。</summary>
    public async Task RequestAsync(string? email, CancellationToken ct = default)
    {
        if (!CommonValidation.IsEmail(email)) return;
        var normalized = users.NormalizeEmail(email);
        var userId = await db.Users.AsNoTracking().Where(x => x.NormalizedEmail == normalized).Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (userId == Guid.Empty) return;

        var issued = await IssueAsync(userId, ct);
        if (issued is null) return;
        await SendAsync(issued, ct);
    }

    /// <summary>期限・改変・消費済みを標準の検証へ委譲し、DBを変更しません。</summary>
    public async Task<bool> ValidateAsync(Guid userId, string? token, PasswordLinkKind kind, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
        return user is not null && IsUsable(user, kind) && await VerifyAsync(user, kind, token);
    }

    /// <summary>入力不正では発行番号を残し、成功時だけ両用途の番号を削除します。</summary>
    public async Task<PasswordLinkResult> ConsumeAsync(Guid userId, string? token, PasswordLinkKind kind, string? password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token)) return PasswordLinkResult.From(PasswordLinkOutcome.InvalidLink);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var user = await db.LockUserAsync(userId, ct);
            if (user is null || !IsUsable(user, kind) || !await VerifyAsync(user, kind, token))
                return PasswordLinkResult.From(PasswordLinkOutcome.InvalidLink);

            // 入力検証を先に行い、条件を満たさない要求でリンクを失効させません。
            var validation = policy.Validate("Password", password);
            if (!validation.IsValid) return new(PasswordLinkOutcome.InvalidInput, validation);

            var applied = kind == PasswordLinkKind.Initial
                ? await SetInitialPasswordAsync(user, token, password!)
                : await users.ResetPasswordAsync(user, token, password!);
            if (!applied.Succeeded) return Failure(applied);

            var stamped = await users.UpdateSecurityStampAsync(user);
            if (!stamped.Succeeded) return Failure(stamped);
            var cleared = await ClearIssuesAsync(user);
            if (!cleared.Succeeded) return Failure(cleared);

            await transaction.CommitAsync(ct);
            return PasswordLinkResult.From(PasswordLinkOutcome.Succeeded);
        }
        catch (DbUpdateConcurrencyException) { return PasswordLinkResult.From(PasswordLinkOutcome.Conflict); }
    }

    /// <summary>受付間隔を確認し、新しい発行番号とトークンを同一トランザクションで確定します。</summary>
    private async Task<IssuedLink?> IssueAsync(Guid userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var user = await db.LockUserAsync(userId, ct);
            if (user is null || !user.IsActive || user.RoleCode is not ("USER" or "ADMIN")) return null;

            var now = clock.GetUtcNow();
            var last = await users.GetAuthenticationTokenAsync(user, PasswordLinkTokens.Provider, PasswordLinkTokens.LastRequestUtc);
            if (DateTimeOffset.TryParse(last, null, System.Globalization.DateTimeStyles.RoundtripKind, out var previous) && now - previous < RequestInterval)
                return null;

            var kind = user.PasswordHash is null ? PasswordLinkKind.Initial : PasswordLinkKind.Reset;
            var accepted = await users.SetAuthenticationTokenAsync(user, PasswordLinkTokens.Provider, PasswordLinkTokens.LastRequestUtc, now.ToString("O"));
            if (!accepted.Succeeded) return null;
            var numbered = await users.SetAuthenticationTokenAsync(user, PasswordLinkTokens.Provider, PasswordLinkTokens.IssueName(kind), PasswordLinkTokens.NewIssue());
            if (!numbered.Succeeded) return null;

            var token = kind == PasswordLinkKind.Initial
                ? await users.GenerateUserTokenAsync(user, PasswordLinkTokens.InitialProviderName, PasswordLinkTokens.InitialPurpose)
                : await users.GeneratePasswordResetTokenAsync(user);
            var expiry = now + (kind == PasswordLinkKind.Initial ? TimeSpan.FromDays(14) : TimeSpan.FromHours(1));
            await transaction.CommitAsync(ct);
            return new IssuedLink(kind, user.Id, user.DisplayName, user.Email ?? "", token, expiry);
        }
        catch (DbUpdateConcurrencyException) { return null; }
    }

    /// <summary>確定済みの内容で一度だけ送信します。失敗はCommonが記録し、再送しません。</summary>
    private async Task SendAsync(IssuedLink issued, CancellationToken ct)
    {
        if (!CommonValidation.IsEmail(issued.Email)) return;
        var path = issued.Kind == PasswordLinkKind.Initial ? "account/password/setup" : "account/password/reset";
        var url = links.Portal($"{path}?user={issued.UserId:N}&token={Uri.EscapeDataString(issued.Token)}");
        var content = templates.Render(PasswordLinkMailTemplates.Key(issued.Kind), new Dictionary<string, string>
        {
            ["表示名"] = issued.DisplayName,
            ["設定URL"] = url,
            ["有効期限"] = clock.ToJst(issued.ExpiresAt).ToString("yyyy/MM/dd HH:mm")
        });
        await mail.SendAsync(new MailRequest([issued.Email], [], [], content.Subject, content.Body), ct);
    }

    /// <summary>初回設定では専用用途で検証し、パスワード追加とメール確認済みを確定します。</summary>
    private async Task<IdentityResult> SetInitialPasswordAsync(ApplicationUser user, string token, string password)
    {
        var added = await users.AddPasswordAsync(user, password);
        if (!added.Succeeded) return added;
        // 社内配布のリンクを受け取れたことをもって、初回設定時にメールアドレスを確認済みとします。
        user.EmailConfirmed = true;
        return await users.UpdateAsync(user);
    }

    /// <summary>用途に対応するTokenProviderで検証します。標準の期限・改変検出へ委譲します。</summary>
    private async Task<bool> VerifyAsync(ApplicationUser user, PasswordLinkKind kind, string token) => kind switch
    {
        PasswordLinkKind.Initial => await users.VerifyUserTokenAsync(user, PasswordLinkTokens.InitialProviderName, PasswordLinkTokens.InitialPurpose, token),
        _ => await users.VerifyUserTokenAsync(user, users.Options.Tokens.PasswordResetTokenProvider, UserManager<ApplicationUser>.ResetPasswordTokenPurpose, token)
    };

    /// <summary>初回用・再設定用の発行番号をまとめて削除します。受付時刻は残します。</summary>
    private async Task<IdentityResult> ClearIssuesAsync(ApplicationUser user)
    {
        var initial = await users.RemoveAuthenticationTokenAsync(user, PasswordLinkTokens.Provider, PasswordLinkTokens.InitialIssue);
        if (!initial.Succeeded) return initial;
        return await users.RemoveAuthenticationTokenAsync(user, PasswordLinkTokens.Provider, PasswordLinkTokens.ResetIssue);
    }

    /// <summary>無効ユーザーと、用途に合わないパスワード設定状態を除外します。</summary>
    private static bool IsUsable(ApplicationUser user, PasswordLinkKind kind) =>
        user.IsActive && user.RoleCode is "USER" or "ADMIN"
        && (kind == PasswordLinkKind.Initial ? user.PasswordHash is null : user.PasswordHash is not null);

    /// <summary>Identityの失敗を競合と入力不正に振り分け、英語の既定文言を画面へ出しません。</summary>
    private static PasswordLinkResult Failure(IdentityResult result) =>
        result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure))
            ? PasswordLinkResult.From(PasswordLinkOutcome.Conflict)
            : PasswordLinkResult.From(PasswordLinkOutcome.InvalidLink);

    /// <summary>送信前に確定した発行内容です。</summary>
    private sealed record IssuedLink(PasswordLinkKind Kind, Guid UserId, string DisplayName, string Email, string Token, DateTimeOffset ExpiresAt);
}
