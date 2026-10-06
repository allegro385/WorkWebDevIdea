using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Logging;
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

/// <summary>外部連携済みの資格情報でログインします。</summary>
public interface IAccountService
{
    /// <summary>ログインIDとパスワードを検証し、成功時だけ共有Cookieを発行します。</summary>
    Task<LoginResult> SignInAsync(string? loginId, string? password, CancellationToken ct = default);
}

/// <summary>Identity標準の検証・ロックを使用し、状態確認後に共有Cookieを発行します。</summary>
public sealed class AccountService(PortalDbContext db, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    ISystemSettingsReader settings, IApplicationClock clock, CurrentUserAccessor current, IActivityLogger activity) : IAccountService
{
    /// <summary>失敗理由を画面で細分化せず、操作ログにだけ区分を残します。</summary>
    public async Task<LoginResult> SignInAsync(string? loginId, string? password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(loginId) || loginId.Length > 256 || string.IsNullOrEmpty(password)) return await RejectAsync("INVALID_CREDENTIALS", ct);
        var normalized = users.NormalizeName(loginId);
        var userId = await db.Users.AsNoTracking().Where(x => x.NormalizedUserName == normalized).Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (userId == Guid.Empty) return await RejectAsync("INVALID_CREDENTIALS", ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null || !user.IsActive || !await db.Roles.AsNoTracking().AnyAsync(x => x.RoleCode == user.RoleCode, ct))
            return await RejectAsync("INACTIVE_USER", ct);

        // 外部同期中に資格情報が変わった場合、検証前の世代と比較してCookieを発行しません。
        var verifiedStamp = user.SecurityStamp;
        var lockedBefore = await users.IsLockedOutAsync(user);
        var result = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!lockedBefore && await users.IsLockedOutAsync(user)) await activity.WriteAsync(new ActivityEvent("ACCOUNT_LOCK", "SUCCESS"), ct);
        if (!result.Succeeded) return await RejectAsync(result.IsLockedOut ? "ACCOUNT_LOCKED" : "INVALID_CREDENTIALS", ct);

        var now = clock.GetUtcNow();
        var verified = await ConfirmAndRecordAccessAsync(userId, verifiedStamp, now, ct);
        if (verified is null) return await RejectAsync("INACTIVE_USER", ct);

        // ログ記録で本人を識別できるよう、Cookie発行前に当該要求の検証済み利用者を確定します。
        current.SetVerified(new CurrentUser(verified.Id, verified.DisplayName, verified.RoleCode));
        await signIn.SignInAsync(verified, SharedCookieContract.CreateSignInProperties(now));
        await activity.WriteAsync(new ActivityEvent("LOGIN", "SUCCESS"), ct);
        var publication = await settings.GetPublicationStatusAsync(ct);
        return new(LoginOutcome.Succeeded, publication == "PRIVATE" && verified.RoleCode != "ADMIN");
    }

    /// <summary>Cookie発行の直前に最新状態を確認し、最終利用日時を確定します。</summary>
    private async Task<ApplicationUser?> ConfirmAndRecordAccessAsync(Guid userId, string? verifiedStamp, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var user = await db.LockUserAsync(userId, ct);
            if (user is null) return null;
            // パスワード検証中の状態変更を反映するため、ロック取得後に最新値を読み直します。
            var entry = db.Entry(user);
            await entry.ReloadAsync(ct);
            if (entry.State == EntityState.Detached || !user.IsActive || user.SecurityStamp != verifiedStamp
                || !await db.Roles.AsNoTracking().AnyAsync(x => x.RoleCode == user.RoleCode, ct)) return null;
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

}
