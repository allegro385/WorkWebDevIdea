using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
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
    /// <param name="isAdmin">限定公開ツールの詳細リンクを表示してよいかどうかです。</param>
    /// <param name="favoritesOnly">お気に入りツールだけを表示するかどうかです。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    Task<ToolListViewModel> GetListAsync(Guid userId, bool isAdmin, bool favoritesOnly, CancellationToken ct = default);

    /// <summary>詳細画面の表示情報を返します。認可は呼出元が事前に判定します。</summary>
    Task<ToolDetailViewModel?> GetDetailAsync(string toolId, Guid userId, CancellationToken ct = default);

    /// <summary>登録済みで同一サイト配下のWeb起動URLだけを返します。</summary>
    Task<string?> GetWebLaunchUrlAsync(string toolId, CancellationToken ct = default);
}

/// <summary>表示できる状態のツールだけを参照し、追跡なしで表示用へ射影します。</summary>
public sealed class ToolQueryService(PortalDbContext db, IApplicationClock clock, IOptions<CommonOptions> options) : IToolQueryService
{
    /// <summary>現在のバージョンが未選択・履歴0件の場合に、画面表示だけを補完する値です。</summary>
    public const string DefaultVersion = "1.0.0";

    /// <summary>非公開ツールは権限にかかわらず一覧へ表示しません。</summary>
    public async Task<ToolListViewModel> GetListAsync(Guid userId, bool isAdmin, bool favoritesOnly, CancellationToken ct = default)
    {
        var rows = await (from tool in db.Tools.AsNoTracking()
                          join category in db.ToolCategories.AsNoTracking() on tool.CategoryId equals category.CategoryId
                          where tool.Status == "PUBLIC" || tool.Status == "PRIVATE"
                          orderby tool.SortOrder, category.SortOrder, tool.ToolName, tool.ToolId
                          select new
                          {
                              tool.ToolId,
                              category.CategoryName,
                              tool.ToolName,
                              tool.ToolSummary,
                              tool.Status,
                              IsFavorite = db.UserToolFavorites.Any(x => x.UserId == userId && x.ToolId == tool.ToolId),
                              Version = db.ToolVersionHistories.Where(x => x.ToolId == tool.ToolId && x.IsCurrent).Select(x => x.Version).FirstOrDefault(),
                              ReleasedAt = db.ToolVersionHistories.Where(x => x.ToolId == tool.ToolId && x.IsCurrent).Select(x => (DateOnly?)x.ReleasedAt).FirstOrDefault()
                          }).ToListAsync(ct);

        var items = rows.Select(row => new ToolListItem(row.ToolId, row.CategoryName, row.ToolName, row.ToolSummary,
            row.Version ?? DefaultVersion, row.Version is null ? null : row.ReleasedAt, row.Status, row.IsFavorite,
            CanOpenDetail: row.Status == "PUBLIC" || isAdmin, IsLimited: row.Status == "PRIVATE")).ToList();

        var favorites = items.Where(x => x.IsFavorite).ToList();
        return new ToolListViewModel(favoritesOnly, items.Count, favorites.Count, favoritesOnly ? favorites : items);
    }

    /// <summary>お知らせ・履歴・提供内容をまとめて取得します。物理パスは表示情報へ含めません。</summary>
    public async Task<ToolDetailViewModel?> GetDetailAsync(string toolId, Guid userId, CancellationToken ct = default)
    {
        var tool = await (from row in db.Tools.AsNoTracking()
                          join category in db.ToolCategories.AsNoTracking() on row.CategoryId equals category.CategoryId
                          where row.ToolId == toolId
                          select new
                          {
                              row.ToolId,
                              category.CategoryName,
                              row.ToolName,
                              row.ToolSummary,
                              row.Remarks,
                              row.ToolType,
                              row.Status,
                              row.WebAppUrl
                          }).SingleOrDefaultAsync(ct);
        if (tool is null) return null;

        var histories = await db.ToolVersionHistories.AsNoTracking().Where(x => x.ToolId == toolId)
            .Select(x => new { x.Version, x.ReleasedAt, x.ChangeDescription, x.IsCurrent }).ToListAsync(ct);
        var current = histories.SingleOrDefault(x => x.IsCurrent);
        List<ToolVersionView> versions = [];
        // 現在のバージョン以下だけを、文字列順ではなく数値3組の比較で降順に並べます。
        if (current is not null && CommonValidation.IsVersion(current.Version))
        {
            var currentVersion = current.Version;
            versions = histories
                .Where(x => CommonValidation.IsVersion(x.Version) && CommonValidation.CompareVersions(x.Version, currentVersion) <= 0)
                .OrderByDescending(x => x.Version, VersionComparer.Instance)
                .Select(x => new ToolVersionView(x.Version, x.ReleasedAt, x.ChangeDescription, x.IsCurrent)).ToList();
        }

        var files = await db.ToolFiles.AsNoTracking().Where(x => x.ToolId == toolId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.FileId)
            .Select(x => new { x.FileId, x.FileCategory, x.DisplayName, x.OriginalFileName, x.FileSizeBytes }).ToListAsync(ct);
        var application = files.Where(x => x.FileCategory == "APP")
            .Select(x => new ToolFileLink(x.FileId, x.DisplayName, x.OriginalFileName, x.FileSizeBytes)).FirstOrDefault();
        var references = files.Where(x => x.FileCategory == "REFERENCE")
            .Select(x => new ToolFileLink(x.FileId, x.DisplayName, x.OriginalFileName, x.FileSizeBytes)).ToList();

        var notices = await NoticeQuery.ReadPublishedAsync(db, clock, "TOOL", toolId, ct);
        var isFavorite = await db.UserToolFavorites.AsNoTracking().AnyAsync(x => x.UserId == userId && x.ToolId == toolId, ct);
        return new ToolDetailViewModel(tool.ToolId, tool.CategoryName, tool.ToolName, tool.ToolSummary, tool.Remarks,
            tool.ToolType, tool.Status, current?.Version ?? DefaultVersion, current?.ReleasedAt, isFavorite,
            notices, versions, tool.ToolType == "WEB" && IsSameSite(tool.WebAppUrl), application, references);
    }

    /// <summary>入力された任意URLへは転送せず、登録済みURLだけを返します。</summary>
    public async Task<string?> GetWebLaunchUrlAsync(string toolId, CancellationToken ct = default)
    {
        var tool = await db.Tools.AsNoTracking().Where(x => x.ToolId == toolId)
            .Select(x => new { x.ToolType, x.WebAppUrl }).SingleOrDefaultAsync(ct);
        if (tool is null || tool.ToolType != "WEB" || !IsSameSite(tool.WebAppUrl)) return null;
        return tool.WebAppUrl;
    }

    /// <summary>Portalと同じスキーム・ホスト・ポートのURLだけを同一サイト配下として許可します。</summary>
    private bool IsSameSite(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var target)) return false;
        if (!Uri.TryCreate(options.Value.PortalBaseUrl, UriKind.Absolute, out var portal)) return false;
        return target.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(target.UserInfo)
            && string.Equals(target.Host, portal.Host, StringComparison.OrdinalIgnoreCase) && target.Port == portal.Port;
    }

    /// <summary>バージョン番号を数値の組として比較する並べ替え用の比較子です。</summary>
    private sealed class VersionComparer : IComparer<string>
    {
        /// <summary>共有できる比較子の実体です。</summary>
        public static readonly VersionComparer Instance = new();

        /// <summary>検証済みのバージョン同士を数値の大小で比較します。</summary>
        public int Compare(string? x, string? y) => CommonValidation.CompareVersions(x ?? "", y ?? "");
    }
}
