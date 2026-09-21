using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Logging;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Authentication;
using SalesSupport.Portal.Web.Data;

namespace SalesSupport.Portal.Web.Services;

/// <summary>ログインの判定結果です。理由は画面で区別しません。</summary>
public enum LoginOutcome
{
    /// <summary>認証に成功し、共有Cookieを発行しました。</summary>
    Succeeded,
    /// <summary>認証できませんでした。理由は共通文言で案内します。</summary>
    Rejected
}

/// <summary>ログイン結果と、入場条件を満たさない場合の案内要否です。</summary>
public sealed record LoginResult(LoginOutcome Outcome, bool RequiresPrivateNotice = false);

/// <summary>パスワード変更の判定結果です。</summary>
public enum PasswordChangeOutcome
{
    /// <summary>変更を確定し、既存の認証を終了しました。</summary>
    Succeeded,
    /// <summary>現在のパスワードが一致しません。</summary>
    InvalidCurrent,
    /// <summary>新しいパスワードが条件を満たしません。</summary>
    InvalidInput,
    /// <summary>対象ユーザーを利用できません。</summary>
    Unavailable,
    /// <summary>並行更新と競合しました。</summary>
    Conflict
}

/// <summary>パスワード変更の結果と項目エラーです。</summary>
public sealed record PasswordChangeResult(PasswordChangeOutcome Outcome, ValidationResult Errors)
{
    /// <summary>項目エラーを伴わない結果を生成します。</summary>
    public static PasswordChangeResult From(PasswordChangeOutcome outcome) => new(outcome, new([]));
}

/// <summary>ログインとパスワード変更を扱います。</summary>
public interface IAccountService
{
    /// <summary>メールとパスワードを検証し、成功時だけ共有Cookieを発行します。</summary>
    Task<LoginResult> SignInAsync(string? email, string? password, CancellationToken ct = default);
    /// <summary>現在のパスワードを確認して新しいパスワードへ変更します。</summary>
    Task<PasswordChangeResult> ChangePasswordAsync(Guid userId, string? currentPassword, string? newPassword, CancellationToken ct = default);
}

/// <summary>Identity標準の検証・ロックを使用し、状態確認後に共有Cookieを発行します。</summary>
public sealed class AccountService(PortalDbContext db, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    IPasswordPolicy policy, ISystemSettingsReader settings, IApplicationClock clock, CurrentUserAccessor current,
    IActivityLogger activity, IHttpContextAccessor http) : IAccountService
{
    /// <summary>失敗理由を画面で細分化せず、操作ログにだけ区分を残します。</summary>
    public async Task<LoginResult> SignInAsync(string? email, string? password, CancellationToken ct = default)
    {
        if (!CommonValidation.IsEmail(email) || string.IsNullOrEmpty(password)) return await RejectAsync("INVALID_CREDENTIALS", ct);
        var normalized = users.NormalizeEmail(email);
        var userId = await db.Users.AsNoTracking().Where(x => x.NormalizedEmail == normalized).Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (userId == Guid.Empty) return await RejectAsync("INVALID_CREDENTIALS", ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null || !user.IsActive || user.RoleCode is not ("USER" or "ADMIN")) return await RejectAsync("INACTIVE_USER", ct);

        var lockedBefore = await users.IsLockedOutAsync(user);
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!lockedBefore && await users.IsLockedOutAsync(user)) await activity.WriteAsync(new ActivityEvent("ACCOUNT_LOCK", "SUCCESS"), ct);
        if (!result.Succeeded) return await RejectAsync(result.IsLockedOut ? "ACCOUNT_LOCKED" : "INVALID_CREDENTIALS", ct);

        var now = clock.GetUtcNow();
        var verified = await ConfirmAndRecordAccessAsync(userId, now, ct);
        if (verified is null) return await RejectAsync("INACTIVE_USER", ct);

        // ログ記録で本人を識別できるよう、Cookie発行前に当該要求の検証済み利用者を確定します。
        current.SetVerified(new CurrentUser(verified.Id, verified.DisplayName, verified.RoleCode));
        await signIn.SignInAsync(verified, SharedCookieContract.CreateSignInProperties(now));
        await activity.WriteAsync(new ActivityEvent("LOGIN", "SUCCESS"), ct);
        var publication = await settings.GetPublicationStatusAsync(ct);
        return new(LoginOutcome.Succeeded, publication == "PRIVATE" && verified.RoleCode != "ADMIN");
    }

    /// <summary>変更を確定し、成功・失敗のいずれも操作ログへ残します。</summary>
    public async Task<PasswordChangeResult> ChangePasswordAsync(Guid userId, string? currentPassword, string? newPassword, CancellationToken ct = default)
    {
        var result = await ApplyPasswordChangeAsync(userId, currentPassword, newPassword, ct);
        await activity.WriteAsync(new ActivityEvent("PASSWORD_UPDATE", ResultCodeOf(result.Outcome), FailureReasonOf(result.Outcome)), ct);
        return result;
    }

    /// <summary>変更確定後に既存リンクを消費し、共有Cookieを破棄します。</summary>
    private async Task<PasswordChangeResult> ApplyPasswordChangeAsync(Guid userId, string? currentPassword, string? newPassword, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(currentPassword)) return PasswordChangeResult.From(PasswordChangeOutcome.InvalidCurrent);
        var validation = policy.Validate("NewPassword", newPassword);
        if (!validation.IsValid) return new(PasswordChangeOutcome.InvalidInput, validation);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var user = await db.LockUserAsync(userId, ct);
            if (user is null || !user.IsActive || user.PasswordHash is null) return PasswordChangeResult.From(PasswordChangeOutcome.Unavailable);

            var changed = await users.ChangePasswordAsync(user, currentPassword, newPassword!);
            if (!changed.Succeeded) return Failure(changed);
            var stamped = await users.UpdateSecurityStampAsync(user);
            if (!stamped.Succeeded) return Failure(stamped);
            foreach (var name in new[] { PasswordLinkTokens.InitialIssue, PasswordLinkTokens.ResetIssue })
            {
                var removed = await users.RemoveAuthenticationTokenAsync(user, PasswordLinkTokens.Provider, name);
                if (!removed.Succeeded) return Failure(removed);
            }
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { return PasswordChangeResult.From(PasswordChangeOutcome.Conflict); }

        // SecurityStamp更新で他アプリのCookieも失効しますが、この要求のCookieは明示的に削除します。
        if (http.HttpContext is { } context) await context.SignOutAsync(SharedCookieContract.Scheme);
        return PasswordChangeResult.From(PasswordChangeOutcome.Succeeded);
    }

    /// <summary>Cookie発行の直前に最新状態を確認し、最終利用日時を確定します。</summary>
    private async Task<ApplicationUser?> ConfirmAndRecordAccessAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var user = await db.LockUserAsync(userId, ct);
            if (user is null) return null;
            // パスワード検証中の状態変更を反映するため、ロック取得後に最新値を読み直します。
            var entry = db.Entry(user);
            await entry.ReloadAsync(ct);
            if (entry.State == EntityState.Detached || !user.IsActive || user.RoleCode is not ("USER" or "ADMIN")) return null;
            user.LastAccessAt = now.UtcDateTime;
            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded) return null;
            await transaction.CommitAsync(ct);
            return user;
        }
        catch (DbUpdateConcurrencyException) { return null; }
    }

    /// <summary>失敗を共通文言へ寄せ、理由は操作ログにだけ残します。</summary>
    private async Task<LoginResult> RejectAsync(string reason, CancellationToken ct)
    {
        await activity.WriteAsync(new ActivityEvent("LOGIN", "FAILURE", reason), ct);
        return new(LoginOutcome.Rejected);
    }

    /// <summary>変更結果を操作ログの結果コードへ変換します。</summary>
    private static string ResultCodeOf(PasswordChangeOutcome outcome) => outcome == PasswordChangeOutcome.Succeeded ? "SUCCESS" : "FAILURE";

    /// <summary>変更失敗の区分を操作ログの理由コードへ変換します。成功時はnullです。</summary>
    private static string? FailureReasonOf(PasswordChangeOutcome outcome) => outcome switch
    {
        PasswordChangeOutcome.Succeeded => null,
        PasswordChangeOutcome.InvalidCurrent => "INVALID_CREDENTIALS",
        PasswordChangeOutcome.InvalidInput => "INVALID_INPUT",
        PasswordChangeOutcome.Conflict => "CONFLICT",
        _ => "INACTIVE_USER"
    };

    /// <summary>Identityの失敗コードを画面の区分へ変換します。英語の既定文言は表示しません。</summary>
    private static PasswordChangeResult Failure(IdentityResult result)
    {
        if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            return PasswordChangeResult.From(PasswordChangeOutcome.InvalidCurrent);
        if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
            return PasswordChangeResult.From(PasswordChangeOutcome.Conflict);
        return PasswordChangeResult.From(PasswordChangeOutcome.Unavailable);
    }
}
