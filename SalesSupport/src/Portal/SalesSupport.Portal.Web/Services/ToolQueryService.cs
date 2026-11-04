using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Models;

namespace SalesSupport.Portal.Web.Services;

/// <summary>利用者向けのツール一覧・詳細・Web起動先を取得します。</summary>
public interface IToolQueryService
{
    /// <summary>一覧掲載できるツールを安定した並び順で返します。</summary>
    /// <param name="userId">お気に入り判定に使用する本人のユーザーIDです。</param>
    /// <param name="roleCode">一覧表示時の一般ロールまたはADMINです。</param>
    /// <param name="favoritesOnly">お気に入りツールだけを表示するかどうかです。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    Task<ToolListViewModel> GetListAsync(Guid userId, string roleCode, bool favoritesOnly, CancellationToken ct = default);

    /// <summary>詳細画面の表示情報を返します。認可は呼出元が事前に判定します。</summary>
    Task<ToolDetailViewModel?> GetDetailAsync(string toolId, Guid userId, CancellationToken ct = default);

    /// <summary>登録済みで同一サイト内のパスのWeb起動先だけを返します。</summary>
    Task<string?> GetWebLaunchUrlAsync(string toolId, CancellationToken ct = default);
}

/// <summary>表示できる状態のツールだけを参照し、追跡なしで表示用へ射影します。</summary>
public sealed class ToolQueryService(PortalDbContext db, IApplicationClock clock) : IToolQueryService
{
    /// <summary>現在のバージョンが未選択・履歴0件の場合に、画面表示だけを補完する値です。</summary>
    public const string DefaultVersion = "1.0.0";

    /// <summary>非公開ツールは権限にかかわらず一覧へ表示しません。</summary>
    public async Task<ToolListViewModel> GetListAsync(Guid userId, string roleCode, bool favoritesOnly, CancellationToken ct = default)
    {
        var isAdmin = roleCode == "ADMIN";
        if (!await db.Roles.AsNoTracking().AnyAsync(x => x.RoleCode == roleCode, ct))
            return new ToolListViewModel(favoritesOnly, 0, 0, []);
        var rows = await (from tool in db.Tools.AsNoTracking()
                          where (tool.Status == "PUBLIC" || tool.Status == "PRIVATE")
                              && (isAdmin || db.ToolRoles.Any(x => x.ToolId == tool.ToolId && x.RoleCode == roleCode))
                          orderby tool.SortOrder, tool.ToolName, tool.ToolId
                          select new
                          {
                              tool.ToolId,
                              tool.ToolName,
                              tool.ToolSummary,
                              tool.Status,
                              tool.ToolType,
                              tool.WebAppUrl,
                              IsFavorite = db.UserToolFavorites.Any(x => x.UserId == userId && x.ToolId == tool.ToolId),
                              Version = db.ToolVersionHistories.Where(x => x.ToolId == tool.ToolId && x.IsCurrent).Select(x => x.Version).FirstOrDefault(),
                              ReleasedAt = db.ToolVersionHistories.Where(x => x.ToolId == tool.ToolId && x.IsCurrent).Select(x => (DateOnly?)x.ReleasedAt).FirstOrDefault()
                          }).ToListAsync(ct);

        var toolIds = rows.Select(x => x.ToolId).ToList();
        var files = await db.ToolFiles.AsNoTracking().Where(x => toolIds.Contains(x.ToolId))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.FileId)
            .Select(x => new { x.ToolId, x.FileId, x.FileCategory, x.DisplayName, x.OriginalFileName, x.FileSizeBytes })
            .ToListAsync(ct);

        var items = rows.Select(row =>
        {
            var canOpen = row.Status == "PUBLIC" || isAdmin;
            var toolFiles = files.Where(x => x.ToolId == row.ToolId).ToList();
            var app = row.ToolType == "DESKTOP" && canOpen
                ? toolFiles.Where(x => x.FileCategory == "APP")
                    .Select(x => new ToolFileLink(x.FileId, x.DisplayName, x.OriginalFileName, x.FileSizeBytes)).FirstOrDefault()
                : null;
            var references = canOpen
                ? toolFiles.Where(x => x.FileCategory == "REFERENCE")
                    .Select(x => new ToolFileLink(x.FileId, x.DisplayName, x.OriginalFileName, x.FileSizeBytes)).ToList()
                : [];
            return new ToolListItem(row.ToolId, row.ToolName, row.ToolSummary,
                row.Version ?? DefaultVersion, row.Version is null ? null : row.ReleasedAt, row.Status, row.IsFavorite,
                canOpen, row.Status == "PRIVATE", canOpen && row.ToolType == "WEB" && CommonValidation.IsLocalPath(row.WebAppUrl), app, references);
        }).ToList();

        var favorites = items.Where(x => x.IsFavorite).ToList();
        return new ToolListViewModel(favoritesOnly, items.Count, favorites.Count, favoritesOnly ? favorites : items);
    }

    /// <summary>お知らせ・履歴・提供内容をまとめて取得します。物理パスは表示情報へ含めません。</summary>
    public async Task<ToolDetailViewModel?> GetDetailAsync(string toolId, Guid userId, CancellationToken ct = default)
    {
        var tool = await db.Tools.AsNoTracking().Where(x => x.ToolId == toolId)
            .Select(x => new { x.ToolId, x.ToolName, x.ToolSummary, x.Remarks, x.ToolType, x.Status, x.WebAppUrl }).SingleOrDefaultAsync(ct);
        if (tool is null) return null;

        var histories = await db.ToolVersionHistories.AsNoTracking().Where(x => x.ToolId == toolId)
            .OrderByDescending(x => x.ReleasedAt).ThenByDescending(x => x.ToolHistoryId)
            .Select(x => new { x.Version, x.ReleasedAt, x.ChangeDescription, x.IsCurrent }).ToListAsync(ct);
        var current = histories.SingleOrDefault(x => x.IsCurrent);
        // 新しい順の一覧から、現在のバージョンより新しい行を除きます。未選択なら履歴は表示しません。
        var versions = histories.SkipWhile(x => !x.IsCurrent)
            .Select(x => new ToolVersionView(x.Version, x.ReleasedAt, x.ChangeDescription, x.IsCurrent)).ToList();

        var files = await db.ToolFiles.AsNoTracking().Where(x => x.ToolId == toolId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.FileId)
            .Select(x => new { x.FileId, x.FileCategory, x.DisplayName, x.OriginalFileName, x.FileSizeBytes }).ToListAsync(ct);
        var application = files.Where(x => x.FileCategory == "APP")
            .Select(x => new ToolFileLink(x.FileId, x.DisplayName, x.OriginalFileName, x.FileSizeBytes)).FirstOrDefault();
        var references = files.Where(x => x.FileCategory == "REFERENCE")
            .Select(x => new ToolFileLink(x.FileId, x.DisplayName, x.OriginalFileName, x.FileSizeBytes)).ToList();

        var notices = await NoticeQuery.ReadPublishedAsync(db, clock, "TOOL", toolId, ct);
        var isFavorite = await db.UserToolFavorites.AsNoTracking().AnyAsync(x => x.UserId == userId && x.ToolId == toolId, ct);
        return new ToolDetailViewModel(tool.ToolId, tool.ToolName, tool.ToolSummary, tool.Remarks,
            tool.ToolType, tool.Status, current?.Version ?? DefaultVersion, current?.ReleasedAt, isFavorite,
            notices, versions, tool.ToolType == "WEB" && CommonValidation.IsLocalPath(tool.WebAppUrl), application, references);
    }

    /// <summary>入力された任意URLへは転送せず、登録済みURLだけを返します。</summary>
    public async Task<string?> GetWebLaunchUrlAsync(string toolId, CancellationToken ct = default)
    {
        var tool = await db.Tools.AsNoTracking().Where(x => x.ToolId == toolId)
            .Select(x => new { x.ToolType, x.WebAppUrl }).SingleOrDefaultAsync(ct);
        if (tool is null || tool.ToolType != "WEB" || !CommonValidation.IsLocalPath(tool.WebAppUrl)) return null;
        return tool.WebAppUrl;
    }
}
