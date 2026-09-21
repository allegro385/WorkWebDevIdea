using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Logging;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;

namespace SalesSupport.Portal.Web.Services;

/// <summary>お気に入りの登録・解除結果です。</summary>
public enum FavoriteOutcome
{
    /// <summary>登録済みへの追加・未登録への解除を含めて成立しました。</summary>
    Succeeded,
    /// <summary>対象ツールを一覧へ表示できないため操作できません。</summary>
    Unavailable
}

/// <summary>本人のお気に入りを登録・解除します。</summary>
public interface IFavoriteService
{
    /// <summary>対象ツールの表示可能状態を再確認してから登録します。</summary>
    Task<FavoriteOutcome> AddAsync(Guid userId, string toolId, CancellationToken ct = default);

    /// <summary>対象ツールの表示可能状態を再確認してから解除します。</summary>
    Task<FavoriteOutcome> RemoveAsync(Guid userId, string toolId, CancellationToken ct = default);
}

/// <summary>複合キーの1行だけを操作し、重複登録と不在解除を成功として扱います。</summary>
public sealed class FavoriteService(PortalDbContext db, IActivityLogger activity) : IFavoriteService
{
    /// <summary>同時実行による重複挿入も成功として扱います。</summary>
    public async Task<FavoriteOutcome> AddAsync(Guid userId, string toolId, CancellationToken ct = default)
    {
        if (!await IsListedAsync(toolId, ct)) return await RecordAsync("FAVORITE_ADD", toolId, FavoriteOutcome.Unavailable, ct);
        if (await db.UserToolFavorites.AsNoTracking().AnyAsync(x => x.UserId == userId && x.ToolId == toolId, ct))
            return await RecordAsync("FAVORITE_ADD", toolId, FavoriteOutcome.Succeeded, ct);

        db.UserToolFavorites.Add(new UserToolFavorite { UserId = userId, ToolId = toolId });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // 同じ利用者の並行登録で主キーが重複した場合も、登録済みとして成功扱いにします。
            db.ChangeTracker.Clear();
            if (!await db.UserToolFavorites.AsNoTracking().AnyAsync(x => x.UserId == userId && x.ToolId == toolId, ct)) throw;
        }
        return await RecordAsync("FAVORITE_ADD", toolId, FavoriteOutcome.Succeeded, ct);
    }

    /// <summary>未登録の解除も成功として扱い、非公開ツールの登録は残します。</summary>
    public async Task<FavoriteOutcome> RemoveAsync(Guid userId, string toolId, CancellationToken ct = default)
    {
        if (!await IsListedAsync(toolId, ct)) return await RecordAsync("FAVORITE_REMOVE", toolId, FavoriteOutcome.Unavailable, ct);
        var favorite = await db.UserToolFavorites.SingleOrDefaultAsync(x => x.UserId == userId && x.ToolId == toolId, ct);
        if (favorite is not null)
        {
            db.UserToolFavorites.Remove(favorite);
            await db.SaveChangesAsync(ct);
        }
        return await RecordAsync("FAVORITE_REMOVE", toolId, FavoriteOutcome.Succeeded, ct);
    }

    /// <summary>一覧へ掲載できる状態のツールかどうかを判定します。</summary>
    private Task<bool> IsListedAsync(string toolId, CancellationToken ct) =>
        db.Tools.AsNoTracking().AnyAsync(x => x.ToolId == toolId && (x.Status == "PUBLIC" || x.Status == "PRIVATE"), ct);

    /// <summary>操作結果を記録し、ログ失敗で本処理の結果を変更しません。</summary>
    private async Task<FavoriteOutcome> RecordAsync(string eventType, string toolId, FavoriteOutcome outcome, CancellationToken ct)
    {
        var succeeded = outcome == FavoriteOutcome.Succeeded;
        await activity.WriteAsync(new ActivityEvent(eventType, succeeded ? "SUCCESS" : "FAILURE",
            succeeded ? null : "TOOL_UNAVAILABLE", "TOOL", toolId), ct);
        return outcome;
    }
}
