using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.FileStorage;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Models;

namespace SalesSupport.Portal.Web.Services;

/// <summary>利用マニュアルPDFの管理された配置です。任意の物理パスは受け付けません。</summary>
public sealed class PortalManualOptions
{
    /// <summary>永続保存領域を基準とした、マニュアルPDFの相対パスです。</summary>
    public string RelativePath { get; set; } = "Manual/sales-support-portal-manual.pdf";

    /// <summary>ダウンロード時に使用するファイル名です。</summary>
    public string FileName { get; set; } = "sales-support-portal-manual.pdf";
}

/// <summary>P005 利用マニュアル・FAQの表示内容と、マニュアルPDFの取得を扱います。</summary>
public interface IHelpService
{
    /// <summary>公開中のFAQをカテゴリ順・項目順で返します。</summary>
    Task<IReadOnlyList<FaqCategoryView>> GetFaqAsync(CancellationToken ct = default);

    /// <summary>管理された配置のマニュアルPDFを開きます。取得できない場合はnullを返します。</summary>
    Task<Stream?> OpenManualAsync(CancellationToken ct = default);

    /// <summary>マニュアルのダウンロード時に使用するファイル名です。</summary>
    string ManualFileName { get; }
}

/// <summary>公開行だけを表示順で読み取り、番号は表示順に採番します。</summary>
public sealed class HelpService(PortalDbContext db, IFileStorage storage, IOptions<PortalManualOptions> manual) : IHelpService
{
    /// <summary>ダウンロード時に使用するファイル名です。</summary>
    public string ManualFileName => manual.Value.FileName;

    /// <summary>非公開のFAQは取得しません。Noは画面の表示順で通し番号にします。</summary>
    public async Task<IReadOnlyList<FaqCategoryView>> GetFaqAsync(CancellationToken ct = default)
    {
        var rows = await (from item in db.FaqItems.AsNoTracking()
                          join category in db.FaqCategories.AsNoTracking() on item.CategoryId equals category.CategoryId
                          where item.IsPublished
                          orderby category.SortOrder, category.CategoryId, item.SortOrder, item.FaqId
                          select new { category.CategoryId, category.CategoryName, item.Question, item.Answer }).ToListAsync(ct);

        var categories = new List<FaqCategoryView>();
        var number = 0;
        foreach (var group in rows.GroupBy(x => new { x.CategoryId, x.CategoryName }))
        {
            var items = new List<FaqEntry>();
            foreach (var row in group) items.Add(new FaqEntry(++number, row.Question, row.Answer));
            categories.Add(new FaqCategoryView(group.Key.CategoryName, items));
        }
        return categories;
    }

    /// <summary>未配置・読取り不能は取得不可として扱い、物理パスを呼出元へ返しません。</summary>
    public async Task<Stream?> OpenManualAsync(CancellationToken ct = default)
    {
        try
        {
            var reference = new StoredFile(manual.Value.RelativePath, ".pdf", 0, manual.Value.FileName);
            return await storage.OpenReadAsync(reference, ct);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
