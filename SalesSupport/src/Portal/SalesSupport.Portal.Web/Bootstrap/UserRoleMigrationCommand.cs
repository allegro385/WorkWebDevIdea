using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Portal.Web.Data;

namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>既存USERをIdentity APIでAへ移し、認証スタンプを失効させます。</summary>
public sealed class UserRoleMigrationCommand(PortalDbContext db, UserManager<ApplicationUser> users, IInitialAdminConsole console)
{
    /// <summary>導入コマンド名です。</summary>
    public const string Name = "migrate-user-roles";

    /// <summary>Web起動とは別の単独コマンド指定を判定します。</summary>
    public static bool IsRequested(string[] args) => args.Length == 1 && args[0] == Name;

    /// <summary>USERを一件ずつ確定し、失敗時は残件を保持して停止します。再実行できます。</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        if (!await db.Roles.AsNoTracking().AnyAsync(x => x.RoleCode == "A", ct))
        {
            console.WriteLine("ロールAが未登録です。003の適用状態を確認してください。");
            return 1;
        }
        if (!await db.Roles.AsNoTracking().AnyAsync(x => x.RoleCode == "USER", ct))
        {
            console.WriteLine("移行用USERロールがありません。移行済みか新規DBです。");
            return 0;
        }
        var ids = await db.Users.AsNoTracking().Where(x => x.RoleCode == "USER").Select(x => x.Id).ToListAsync(ct);
        var count = 0;
        foreach (var id in ids)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null || user.RoleCode != "USER")
            {
                console.WriteLine("対象ユーザーの状態が変わりました。移行を停止します。");
                return 1;
            }
            user.RoleCode = "A";
            if (!(await users.UpdateAsync(user)).Succeeded || !(await users.UpdateSecurityStampAsync(user)).Succeeded)
            {
                console.WriteLine("Identityの更新に失敗しました。移行を停止します。");
                return 1;
            }
            await transaction.CommitAsync(ct);
            count++;
        }
        console.WriteLine($"移行完了: {count}件。004を適用してください。");
        return 0;
    }
}
