using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Data;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;

namespace SalesSupport.Common.UI;

/// <summary>ヘッダーのリンク1件です。</summary>
public sealed record HeaderLink(string Label, string Url);

/// <summary>ヘッダーのドロップダウン1組です。項目は同じ幅で表示します。</summary>
public sealed record HeaderMenu(string Label, IReadOnlyList<HeaderLink> Links);

/// <summary>共通ヘッダーの表示内容です。権限は検証済み情報だけから決定します。</summary>
public sealed record SalesSupportHeaderModel(string? DisplayName, bool IsAdmin, bool IsDevelopment, string PortalTopUrl,
    IReadOnlyList<HeaderMenu> Menus, IReadOnlyList<HeaderLink> AccountLinks, string LogoutUrl);

/// <summary>検証済みユーザー、環境帯、Portalの固定リンクを描画します。</summary>
public sealed class SalesSupportHeaderViewComponent(ICurrentUserAccessor current, IOptions<CommonOptions> options, ISalesSupportLinks links, CommonDbContext db) : ViewComponent
{
    /// <summary>管理メニューの表示可否は表示上の制御であり、認可の代替にはしません。</summary>
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var user = current.User;
        var isAdmin = user?.RoleCode == "ADMIN";
        // 未検証の利用者と、入場制限案内・認証画面には通常機能への導線を表示しません。
        if (user is null || !ViewData.GetPageShell().ShowCommonMenus)
            return View(new SalesSupportHeaderModel(null, false, options.Value.IsDevelopment, links.Portal(), [], [], links.Local("account/logout")));
        var canNotify = await db.Roles.AsNoTracking().AnyAsync(role => role.RoleCode == user.RoleCode && role.NoticeMailEnabled, HttpContext.RequestAborted);
        List<HeaderLink> support = [];
        if (canNotify) support.Add(new("個人設定", links.Portal("preferences")));
        support.Add(new("利用マニュアル・FAQ", links.Portal("help")));
        support.Add(new("問い合わせ・ご意見", links.Portal("inquiries/new")));
        List<HeaderMenu> menus =
        [
            new("ツール一覧", [new("全ツール", links.Portal("tools")), new("お気に入りツール", links.Portal("tools/favorites"))]),
            new("サポート", support)
        ];
        if (isAdmin)
            menus.Add(new("管理者用画面",
            [
                new("ユーザー管理", links.Portal("admin/users")), new("問い合わせ管理", links.Portal("admin/inquiries")),
                new("ツール管理", links.Portal("admin/tools")), new("サイト管理", links.Portal("admin/notices"))
            ]));
        List<HeaderLink> accountLinks = [];
        return View(new SalesSupportHeaderModel(user?.DisplayName, isAdmin, options.Value.IsDevelopment,
            links.Portal(), menus, accountLinks, links.Local("account/logout")));
    }
}
