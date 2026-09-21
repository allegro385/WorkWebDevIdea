using Microsoft.EntityFrameworkCore;
using SalesSupport.Portal.Web.Entities;

namespace SalesSupport.Portal.Web.Data;

/// <summary>ツール単位の履歴・ファイル更新を短いトランザクション内で直列化します。</summary>
public static class PortalToolLocks
{
    /// <summary>対象のToolsの行を更新ロック付きで読み取り、最新のEntityを追跡状態で返します。</summary>
    /// <param name="db">更新に使用するContextです。</param>
    /// <param name="toolId">直列化の対象とするツールです。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>存在しない場合はnullを返します。</returns>
    public static Task<Tool?> LockToolAsync(this PortalDbContext db, string toolId, CancellationToken ct) =>
        db.Tools.FromSql($"SELECT * FROM portal.Tools WITH (UPDLOCK, HOLDLOCK) WHERE ToolId = {toolId}").SingleOrDefaultAsync(ct);
}
