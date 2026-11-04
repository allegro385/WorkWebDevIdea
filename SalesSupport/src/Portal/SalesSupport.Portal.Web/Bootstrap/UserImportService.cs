using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;

namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>追加または更新する1件です。</summary>
/// <param name="Row">TSVの内容です。</param>
/// <param name="Existing">更新対象のユーザーです。追加ではNULLです。</param>
public sealed record UserImportChange(UserImportRow Row, ApplicationUser? Existing);

/// <summary>DBと照合した登録計画です。</summary>
/// <param name="Changes">保存する追加・更新です。</param>
/// <param name="Skipped">既存または差分なしで保存しない件数です。</param>
/// <param name="Errors">行番号と理由です。入力値は含めません。</param>
public sealed record UserImportPlan(IReadOnlyList<UserImportChange> Changes, int Skipped, IReadOnlyList<string> Errors)
{
    /// <summary>追加する件数です。</summary>
    public int Added => Changes.Count(x => x.Existing is null);

    /// <summary>更新する件数です。</summary>
    public int Updated => Changes.Count(x => x.Existing is not null);
}

/// <summary>TSVの内容をDBと照合し、Identity API経由で全件を1トランザクションで保存します。</summary>
public sealed class UserImportService(PortalDbContext db, UserManager<ApplicationUser> users)
{
    /// <summary>DBを変更せず、追加・更新・スキップと不備を判定します。</summary>
    /// <param name="rows">形式を検証済みの行です。</param>
    /// <param name="update">既存ユーザーを更新する場合はtrueです。</param>
    /// <param name="ct">処理のキャンセルトークンです。</param>
    /// <returns>登録計画です。</returns>
    public async Task<UserImportPlan> PlanAsync(IReadOnlyList<UserImportRow> rows, bool update, CancellationToken ct = default)
    {
        var roles = (await db.Roles.AsNoTracking().Select(x => x.RoleCode).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        List<UserImportChange> changes = [];
        List<string> errors = [];
        var skipped = 0;
        foreach (var row in rows)
        {
            var existing = await users.FindByNameAsync(row.LoginId);
            if (existing is not null && (!update || !HasChanges(existing, row)))
            {
                skipped++;
                continue;
            }
            if (await FindErrorAsync(row, existing, roles) is { } reason) errors.Add($"{row.LineNumber}行目：{reason}");
            else changes.Add(new(row, existing));
        }
        return new(changes, skipped, errors);
    }

    /// <summary>計画の全件を1つのトランザクションで保存します。途中で失敗したら全件を戻します。</summary>
    /// <param name="plan">不備のない登録計画です。</param>
    /// <param name="ct">処理のキャンセルトークンです。</param>
    /// <returns>失敗した行番号です。成功ならNULLです。</returns>
    public async Task<int?> ApplyAsync(UserImportPlan plan, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var change in plan.Changes)
        {
            bool saved;
            try { saved = change.Existing is null ? await CreateAsync(change.Row, ct) : await UpdateAsync(change.Existing, change.Row); }
            catch (DbUpdateException) { saved = false; }
            if (!saved) return change.Row.LineNumber;
        }
        await transaction.CommitAsync(ct);
        return null;
    }

    /// <summary>既存ユーザーとの差分があるかを判定します。パスワードはハッシュと比較せず、指定があれば差分とします。</summary>
    /// <param name="user">既存ユーザーです。</param>
    /// <param name="row">TSVの内容です。</param>
    /// <returns>更新が必要ならtrueです。</returns>
    private static bool HasChanges(ApplicationUser user, UserImportRow row) => row.Password.Length != 0 || user.DisplayName != row.DisplayName || RequiresNewStamp(user, row);

    /// <summary>既存のセッションを無効にする項目が変わるかを判定します。パスワードの変更はIdentityが更新します。</summary>
    /// <param name="user">既存ユーザーです。</param>
    /// <param name="row">TSVの内容です。</param>
    /// <returns>SecurityStampの更新が必要ならtrueです。</returns>
    private static bool RequiresNewStamp(ApplicationUser user, UserImportRow row) => user.Email != row.Email || user.RoleCode != row.RoleCode || user.IsActive != row.IsActive;

    /// <summary>ロール、メールの重複、パスワードをDBとIdentityの条件で検証します。</summary>
    /// <param name="row">TSVの内容です。</param>
    /// <param name="existing">更新対象のユーザーです。追加ではNULLです。</param>
    /// <param name="roles">登録済みのロールです。</param>
    /// <returns>不備の理由です。問題がなければNULLです。</returns>
    private async Task<string?> FindErrorAsync(UserImportRow row, ApplicationUser? existing, HashSet<string> roles)
    {
        if (!roles.Contains(row.RoleCode)) return "ロールが登録されていません。";
        var sameEmail = await users.FindByEmailAsync(row.Email);
        if (sameEmail is not null && sameEmail.Id != existing?.Id) return "メールアドレスを別のユーザーが使用しています。";
        if (row.Password.Length == 0) return existing is null ? "新規ユーザーにはパスワードが必要です。" : null;
        foreach (var validator in users.PasswordValidators)
            if (!(await validator.ValidateAsync(users, existing ?? new ApplicationUser(), row.Password)).Succeeded)
                return "パスワードが入力条件を満たしていません。";
        return null;
    }

    /// <summary>ユーザーと通知OFFの利用者設定を作成します。</summary>
    /// <param name="row">TSVの内容です。</param>
    /// <param name="ct">処理のキャンセルトークンです。</param>
    /// <returns>保存できた場合はtrueです。</returns>
    private async Task<bool> CreateAsync(UserImportRow row, CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = row.LoginId,
            Email = row.Email,
            EmailConfirmed = true,
            DisplayName = row.DisplayName,
            RoleCode = row.RoleCode,
            IsActive = row.IsActive,
            LockoutEnabled = true
        };
        if (!(await users.CreateAsync(user, row.Password)).Succeeded) return false;
        db.UserPreferences.Add(new UserPreference { UserId = user.Id, SystemNoticeMailEnabled = false, FavoriteToolNoticeMailEnabled = false });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>既存ユーザーを更新します。本人の通知設定は変更しません。</summary>
    /// <param name="user">更新対象のユーザーです。</param>
    /// <param name="row">TSVの内容です。</param>
    /// <returns>保存できた場合はtrueです。</returns>
    private async Task<bool> UpdateAsync(ApplicationUser user, UserImportRow row)
    {
        var requiresNewStamp = RequiresNewStamp(user, row);
        user.Email = row.Email;
        user.EmailConfirmed = true;
        user.DisplayName = row.DisplayName;
        user.RoleCode = row.RoleCode;
        user.IsActive = row.IsActive;
        if (!(await users.UpdateAsync(user)).Succeeded) return false;
        if (row.Password.Length != 0)
            return (await users.RemovePasswordAsync(user)).Succeeded && (await users.AddPasswordAsync(user, row.Password)).Succeeded;
        return !requiresNewStamp || (await users.UpdateSecurityStampAsync(user)).Succeeded;
    }
}
