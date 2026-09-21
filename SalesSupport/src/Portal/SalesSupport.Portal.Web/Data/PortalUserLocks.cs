using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Entities.Identity;

namespace SalesSupport.Portal.Web.Data;

/// <summary>ユーザー単位の発行・消費・状態変更を短いトランザクション内で直列化します。</summary>
public static class PortalUserLocks
{
    /// <summary>対象のAspNetUsers行を更新ロック付きで読み取り、最新のEntityを追跡状態で返します。</summary>
    /// <param name="db">UserManagerのStoreと同一のContextです。</param>
    /// <param name="userId">直列化の対象とするユーザーです。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>存在しない場合はnullを返します。</returns>
    public static Task<ApplicationUser?> LockUserAsync(this PortalDbContext db, Guid userId, CancellationToken ct) =>
        db.Users.FromSql($"SELECT * FROM portal.AspNetUsers WITH (UPDLOCK, HOLDLOCK) WHERE UserId = {userId}").SingleOrDefaultAsync(ct);
}
