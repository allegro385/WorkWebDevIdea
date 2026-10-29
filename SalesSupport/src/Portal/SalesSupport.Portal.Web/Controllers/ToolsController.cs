using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Controllers;

/// <summary>P003 ツール一覧、P008 お気に入りツールおよびP004 ツール詳細を表示します。</summary>
[Route("tools")]
public sealed class ToolsController(IToolQueryService tools, ICurrentUserAccessor current, IAccessEvaluator access, ISystemNoticeService notices,
    IOptions<CommonOptions> options) : Controller
{
    /// <summary>全ツールを表示します。</summary>
    [HttpGet("")]
    public Task<IActionResult> Index(CancellationToken ct) => ListAsync(favoritesOnly: false, ct);

    /// <summary>本人が登録したお気に入りツールだけを表示します。</summary>
    [HttpGet("favorites")]
    public Task<IActionResult> Favorites(CancellationToken ct) => ListAsync(favoritesOnly: true, ct);

    /// <summary>ツール詳細を表示します。状態ごとの可否は共通利用制御で判定します。</summary>
    /// <param name="toolId">対象ツールです。</param>
    /// <param name="favorites">お気に入り一覧から遷移した場合はtrueにし、戻り先を切り替えます。匿名モードでは一覧を提供しないため無視します。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    [HttpGet("{toolId}")]
    public async Task<IActionResult> Detail(string toolId, bool favorites, CancellationToken ct)
    {
        if (current.User is not { } user) return Unauthorized();
        var decision = await access.EvaluateAsync(new AccessRequest(AccessPurpose.ToolDetail, toolId), ct);
        if (!decision.Allowed) return StatusCode(decision.StatusCode);

        var detail = await tools.GetDetailAsync(toolId, user.UserId, ct);
        if (detail is null) return NotFound();
        var anonymous = options.Value.IsAnonymous;
        // 匿名モードはお気に入り一覧を経路ごと提供しないため、戻り先をツール一覧へ固定します。
        var fromFavorites = favorites && !anonymous;
        var listPath = fromFavorites ? "tools/favorites" : "tools";
        var listLabel = fromFavorites ? "お気に入りツール" : "ツール一覧";
        ViewData.SetPageShell(new PageShellModel
        {
            PageTitle = "ツール詳細",
            Breadcrumbs = [new Breadcrumb("トップ", ""), new Breadcrumb(listLabel, listPath), new Breadcrumb("ツール詳細")]
        });
        return View("Detail", detail with { FromFavorites = fromFavorites, IsAnonymousMode = anonymous });
    }

    /// <summary>登録済みの同一サイト内のパスへだけ遷移します。利用ログは起動先で記録します。</summary>
    [HttpGet("{toolId}/launch")]
    public async Task<IActionResult> Launch(string toolId, CancellationToken ct)
    {
        var decision = await access.EvaluateAsync(new AccessRequest(AccessPurpose.ToolUse, toolId), ct);
        if (!decision.Allowed) return StatusCode(decision.StatusCode);

        var url = await tools.GetWebLaunchUrlAsync(toolId, ct);
        return url is null ? NotFound() : LocalRedirect(url);
    }

    /// <summary>全ツールとお気に入りで同じ表示項目・並び順・操作を使用します。</summary>
    private async Task<IActionResult> ListAsync(bool favoritesOnly, CancellationToken ct)
    {
        if (current.User is not { } user) return Unauthorized();
        var anonymous = options.Value.IsAnonymous;
        var model = await tools.GetListAsync(user.UserId, user.RoleCode, favoritesOnly, ct);
        // 匿名モードはトップ画面を設けないため、この画面でお知らせを表示します。同じPortalDbContextを使うので順に取得します。
        var published = anonymous ? await notices.GetPublishedAsync(ct) : [];
        var title = favoritesOnly ? "お気に入りツール" : "ツール一覧";
        ViewData.SetPageShell(new PageShellModel
        {
            PageTitle = title,
            UseTitleBand = true,
            Breadcrumbs = [new Breadcrumb("トップ", ""), new Breadcrumb(title)]
        });
        return View("Index", model with { Notices = published, IsAnonymousMode = anonymous });
    }
}
