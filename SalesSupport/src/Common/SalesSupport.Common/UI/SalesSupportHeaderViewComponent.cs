using Microsoft.AspNetCore.Mvc;
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
public sealed class SalesSupportHeaderViewComponent(ICurrentUserAccessor current, IOptions<CommonOptions> options, ISalesSupportLinks links) : ViewComponent
{
    /// <summary>管理メニューの表示可否は表示上の制御であり、認可の代替にはしません。</summary>
    public IViewComponentResult Invoke()
    {
        var user = current.User;
        var isAdmin = user?.RoleCode == "ADMIN";
        List<HeaderMenu> menus =
        [
            new("ツール一覧", [new("全ツール", links.Portal("tools")), new("お気に入りツール", links.Portal("tools/favorites"))]),
            new("サポート", [new("個人設定", links.Portal("preferences")), new("利用マニュアル・FAQ", links.Portal("help")), new("問い合わせ・ご意見", links.Portal("inquiries/new"))])
        ];
        if (isAdmin)
            menus.Add(new("管理者用画面",
            [
                new("ユーザー管理", links.Portal("admin/users")), new("問い合わせ管理", links.Portal("admin/inquiries")),
                new("ツール管理", links.Portal("admin/tools")), new("サイト管理", links.Portal("admin/notices")),
                new("ログ管理", links.Portal("admin/logs"))
            ]));
        List<HeaderLink> accountLinks = [new("パスワード変更", links.Portal("account/password/change"))];
        return View(new SalesSupportHeaderModel(user?.DisplayName, isAdmin, options.Value.IsDevelopment,
            links.Portal(), menus, accountLinks, links.Local("account/logout")));
    }
}
