using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Logging;
using SalesSupport.Common.MasterData;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Areas.Admin.Models;
using SalesSupport.Portal.Web.Data;

namespace SalesSupport.Portal.Web.Services;

/// <summary>ユーザー管理の保存結果です。</summary>
public enum UserAdminOutcome
{
    /// <summary>更新しました。</summary>
    Saved,
    /// <summary>入力が条件を満たしません。</summary>
    InvalidInput,
    /// <summary>ほかの操作で更新されていたため保存しませんでした。</summary>
    Conflict,
    /// <summary>対象ユーザーを取得できません。</summary>
    Unavailable
}

/// <summary>保存結果と項目エラーです。</summary>
/// <param name="Outcome">保存の判定結果です。</param>
/// <param name="Errors">入力不正時の項目エラーです。</param>
public sealed record UserAdminResult(UserAdminOutcome Outcome, ValidationResult Errors)
{
    /// <summary>項目エラーを伴わない結果を生成します。</summary>
    public static UserAdminResult From(UserAdminOutcome outcome) => new(outcome, new([]));

    /// <summary>項目エラーを伴う入力不正の結果を生成します。</summary>
    public static UserAdminResult Invalid(FieldError error) => new(UserAdminOutcome.InvalidInput, new([error]));
}

/// <summary>A002 ユーザー管理の検索・編集・ロック解除を扱います。</summary>
public interface IUserAdminService
{
    /// <summary>検索条件に一致するユーザーを返します。無効なユーザーも表示します。</summary>
    Task<IReadOnlyList<UserListItem>> SearchAsync(UserSearchInput input, CancellationToken ct = default);

    /// <summary>DBのロール定義から選択肢を取得します。</summary>
    Task<IReadOnlyList<CodeOption>> GetRolesAsync(bool includeAdmin, CancellationToken ct = default);

    /// <summary>編集画面の表示情報を返します。</summary>
    Task<UserEditViewModel?> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>有効状態と一般ロールを更新します。</summary>
    Task<UserAdminResult> UpdateAsync(Guid operatorUserId, UserEditInput input, CancellationToken ct = default);

    /// <summary>ロック終了日時と失敗回数を同時に解除します。有効状態は変更しません。</summary>
    Task<UserAdminResult> UnlockAsync(Guid userId, string? concurrencyStamp, CancellationToken ct = default);
}

/// <summary>Identity APIとユーザー行の直列化で、管理者の更新を確定します。</summary>
public sealed class UserAdminService(PortalDbContext db, UserManager<ApplicationUser> users, IApplicationClock clock,
    IActivityLogger activity) : IUserAdminService
{
    /// <summary>ロック中はLockoutEnabledが有効で、ロック終了日時が現在日時より後の状態とします。</summary>
    public async Task<IReadOnlyList<UserListItem>> SearchAsync(UserSearchInput input, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var query = db.Users.AsNoTracking().AsQueryable();
        if (CommonValidation.Normalize(input.LoginId) is { } loginId) query = query.Where(x => x.UserName != null && x.UserName.Contains(loginId));
        if (CommonValidation.Normalize(input.DisplayName) is { } displayName) query = query.Where(x => x.DisplayName.Contains(displayName));
        if (CommonValidation.Normalize(input.Email) is { } email) query = query.Where(x => x.Email != null && x.Email.Contains(email));
        if (!string.IsNullOrWhiteSpace(input.RoleCode)) query = query.Where(x => x.RoleCode == input.RoleCode);
        if (input.State == "ACTIVE") query = query.Where(x => x.IsActive);
        if (input.State == "INACTIVE") query = query.Where(x => !x.IsActive);
        if (input.LockState == "LOCKED") query = query.Where(x => x.LockoutEnabled && x.LockoutEnd != null && x.LockoutEnd > now);
        if (input.LockState == "UNLOCKED") query = query.Where(x => !x.LockoutEnabled || x.LockoutEnd == null || x.LockoutEnd <= now);

        var rows = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Email)
            .Select(x => new { x.Id, x.UserName, x.DisplayName, x.Email, x.RoleCode, x.IsActive, x.LockoutEnabled, x.LockoutEnd, x.LastAccessAt })
            .ToListAsync(ct);
        var roleNames = await db.Roles.AsNoTracking().ToDictionaryAsync(x => x.RoleCode, x => x.RoleName, ct);
        return rows.Select(x => new UserListItem(x.Id, x.UserName ?? "", x.DisplayName, x.Email ?? "", x.RoleCode,
            roleNames.GetValueOrDefault(x.RoleCode, x.RoleCode), x.IsActive,
            IsLocked(x.LockoutEnabled, x.LockoutEnd, now), ToJst(x.LastAccessAt))).ToList();
    }

    /// <summary>ADMINを含めるか指定し、コード順の選択肢を返します。</summary>
    public async Task<IReadOnlyList<CodeOption>> GetRolesAsync(bool includeAdmin, CancellationToken ct = default) =>
        await db.Roles.AsNoTracking().Where(x => includeAdmin || x.RoleCode != "ADMIN")
            .OrderBy(x => x.RoleCode).Select(x => new CodeOption(x.RoleCode, x.RoleName, 0, null)).ToListAsync(ct);

    /// <summary>ロック状態、失敗回数および資格情報の連携状態もあわせて表示します。</summary>
    public async Task<UserEditViewModel?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null) return null;
        var now = clock.GetUtcNow();
        return new UserEditViewModel
        {
            Input = new UserEditInput
            {
                UserId = user.Id,
                IsActive = user.IsActive,
                RoleCode = user.RoleCode,
                ConcurrencyStamp = user.ConcurrencyStamp
            },
            LoginId = user.UserName ?? "",
            DisplayName = user.DisplayName,
            Email = user.Email ?? "",
            RoleCode = user.RoleCode,
            RoleName = await db.Roles.AsNoTracking().Where(x => x.RoleCode == user.RoleCode).Select(x => x.RoleName).SingleOrDefaultAsync(ct) ?? user.RoleCode,
            GeneralRoles = await GetRolesAsync(false, ct),
            IsLocked = IsLocked(user.LockoutEnabled, user.LockoutEnd, now),
            LockoutEnd = user.LockoutEnd is { } lockoutEnd ? clock.ToJst(lockoutEnd) : null,
            AccessFailedCount = user.AccessFailedCount,
            HasPassword = user.PasswordHash is not null,
            LastAccessAt = ToJst(user.LastAccessAt)
        };
    }

    /// <summary>更新の成否を操作ログへ残します。</summary>
    public async Task<UserAdminResult> UpdateAsync(Guid operatorUserId, UserEditInput input, CancellationToken ct = default)
    {
        var result = await ApplyUpdateAsync(operatorUserId, input, ct);
        await WriteAsync("USER_UPDATE", input.UserId, result, ct,
            result.Outcome == UserAdminOutcome.Saved
                ? [new ActivityChange("IsActive", null, input.IsActive ? "1" : "0"), new ActivityChange("RoleCode", null, input.RoleCode)]
                : null);
        return result;
    }

    /// <summary>無効化時は管理者数の検査を直列化し、自動再試行せず再読込を促します。</summary>
    private async Task<UserAdminResult> ApplyUpdateAsync(Guid operatorUserId, UserEditInput input, CancellationToken ct)
    {
        // 無効化では複数ユーザーにまたがる管理者数を検査するため、直列化したトランザクションを使用します。
        var isolation = input.IsActive ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable;
        await using var transaction = await db.Database.BeginTransactionAsync(isolation, ct);
        try
        {
            var user = await db.LockUserAsync(input.UserId, ct);
            if (user is null) return UserAdminResult.From(UserAdminOutcome.Unavailable);
            if (user.ConcurrencyStamp != input.ConcurrencyStamp) return UserAdminResult.From(UserAdminOutcome.Conflict);

            if (user.RoleCode == "ADMIN")
            {
                if (input.RoleCode != "ADMIN") return UserAdminResult.Invalid(new(nameof(UserEditInput.RoleCode), "INVALID_INPUT", "管理者ロールは変更できません。"));
            }
            else if (input.RoleCode == "ADMIN" || string.IsNullOrWhiteSpace(input.RoleCode)
                || !await db.Roles.AsNoTracking().AnyAsync(x => x.RoleCode == user.RoleCode, ct)
                || !await db.Roles.AsNoTracking().AnyAsync(x => x.RoleCode == input.RoleCode && x.RoleCode != "ADMIN", ct))
                return UserAdminResult.Invalid(new(nameof(UserEditInput.RoleCode), "INVALID_INPUT", "DBに登録された一般ロールを選択してください。"));

            if (user.IsActive && !input.IsActive && await RejectDeactivationAsync(operatorUserId, user, ct) is { } rejected) return rejected;

            var activeChanged = user.IsActive != input.IsActive;
            var roleChanged = user.RoleCode != input.RoleCode;
            user.IsActive = input.IsActive;
            user.RoleCode = input.RoleCode!;
            if (!(await users.UpdateAsync(user)).Succeeded) return UserAdminResult.From(UserAdminOutcome.Conflict);

            // 外部連携が所有するログインID・表示名・メール・パスワードを上書きしません。
            if (activeChanged || roleChanged)
                if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) return UserAdminResult.From(UserAdminOutcome.Conflict);
            await transaction.CommitAsync(ct);
            return UserAdminResult.From(UserAdminOutcome.Saved);
        }
        catch (DbUpdateConcurrencyException) { return UserAdminResult.From(UserAdminOutcome.Conflict); }
    }

    /// <summary>自分自身・最後の有効管理者・担当中の対象がある場合は無効化を拒否します。</summary>
    private async Task<UserAdminResult?> RejectDeactivationAsync(Guid operatorUserId, ApplicationUser user, CancellationToken ct)
    {
        if (user.Id == operatorUserId)
            return UserAdminResult.Invalid(new(nameof(UserEditInput.IsActive), "INVALID_INPUT", "ログイン中の管理者自身を無効化できません。"));
        if (user.RoleCode == "ADMIN" && !await db.Users.AsNoTracking().AnyAsync(x => x.IsActive && x.RoleCode == "ADMIN" && x.Id != user.Id, ct))
            return UserAdminResult.Invalid(new(nameof(UserEditInput.IsActive), "INVALID_INPUT", "最後の有効なシステム管理者は無効化できません。"));
        if (await db.Tools.AsNoTracking().AnyAsync(x => x.OwnerUserId == user.Id, ct))
            return UserAdminResult.Invalid(new(nameof(UserEditInput.IsActive), "INVALID_INPUT", "ツールの担当者に設定されています。先に担当を引き継いでください。"));
        if (await db.Inquiries.AsNoTracking().AnyAsync(x => x.AssigneeUserId == user.Id && x.Status != "COMPLETED" && x.Status != "NO_ACTION", ct))
            return UserAdminResult.Invalid(new(nameof(UserEditInput.IsActive), "INVALID_INPUT", "対応中の問い合わせの担当者に設定されています。先に担当を引き継いでください。"));
        return null;
    }

    /// <summary>ロック解除の成否を操作ログへ残します。</summary>
    public async Task<UserAdminResult> UnlockAsync(Guid userId, string? concurrencyStamp, CancellationToken ct = default)
    {
        var result = await ApplyUnlockAsync(userId, concurrencyStamp, ct);
        await WriteAsync("USER_UNLOCK", userId, result, ct);
        return result;
    }

    /// <summary>ロック終了日時と失敗回数だけを同時に更新します。</summary>
    private async Task<UserAdminResult> ApplyUnlockAsync(Guid userId, string? concurrencyStamp, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var user = await db.LockUserAsync(userId, ct);
            if (user is null) return UserAdminResult.From(UserAdminOutcome.Unavailable);
            if (user.ConcurrencyStamp != concurrencyStamp) return UserAdminResult.From(UserAdminOutcome.Conflict);

            if (!(await users.SetLockoutEndDateAsync(user, null)).Succeeded) return UserAdminResult.From(UserAdminOutcome.Conflict);
            if (!(await users.ResetAccessFailedCountAsync(user)).Succeeded) return UserAdminResult.From(UserAdminOutcome.Conflict);
            await transaction.CommitAsync(ct);
            return UserAdminResult.From(UserAdminOutcome.Saved);
        }
        catch (DbUpdateConcurrencyException) { return UserAdminResult.From(UserAdminOutcome.Conflict); }
    }

    /// <summary>ロック中かどうかを共通の条件で判定します。</summary>
    private static bool IsLocked(bool lockoutEnabled, DateTimeOffset? lockoutEnd, DateTimeOffset now) =>
        lockoutEnabled && lockoutEnd is { } end && end > now;

    /// <summary>保存されたUTC日時を画面表示用のJSTへ変換します。</summary>
    private DateTimeOffset? ToJst(System.DateTime? utc) =>
        utc is null ? null : clock.ToJst(new DateTimeOffset(System.DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc)));

    /// <summary>保存結果を操作ログへ記録します。ログ失敗で保存結果は変更しません。</summary>
    private async Task WriteAsync(string eventType, Guid userId, UserAdminResult result, CancellationToken ct,
        IReadOnlyList<ActivityChange>? changes = null)
    {
        var succeeded = result.Outcome == UserAdminOutcome.Saved;
        await activity.WriteAsync(new ActivityEvent(eventType, succeeded ? "SUCCESS" : "FAILURE",
            succeeded ? null : result.Outcome switch
            {
                UserAdminOutcome.Conflict => "CONFLICT",
                UserAdminOutcome.InvalidInput => "INVALID_INPUT",
                _ => "INACTIVE_USER"
            }, "USER", userId.ToString("N"), changes), ct);
    }
}
