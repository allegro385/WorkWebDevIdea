using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Controllers;

/// <summary>本人のお気に入り登録・解除を受け付けます。詳細画面へは遷移しません。</summary>
[Route("tools/{toolId}")]
public sealed class FavoritesController(IFavoriteService favorites, ICurrentUserAccessor current) : Controller
{
    /// <summary>お気に入りへ登録します。登録済みでも成功として元の画面へ戻ります。</summary>
    [HttpPost("favorite")]
    public Task<IActionResult> Add(string toolId, string? returnUrl, CancellationToken ct) =>
        ApplyAsync(toolId, returnUrl, add: true, ct);

    /// <summary>お気に入りを解除します。未登録でも成功として元の画面へ戻ります。</summary>
    [HttpPost("unfavorite")]
    public Task<IActionResult> Remove(string toolId, string? returnUrl, CancellationToken ct) =>
        ApplyAsync(toolId, returnUrl, add: false, ct);

    /// <summary>登録・解除の結果にかかわらず、操作元のサイト内画面へ戻します。</summary>
    private async Task<IActionResult> ApplyAsync(string toolId, string? returnUrl, bool add, CancellationToken ct)
    {
        if (current.User is not { } user) return Unauthorized();
        var outcome = add ? await favorites.AddAsync(user.UserId, toolId, ct) : await favorites.RemoveAsync(user.UserId, toolId, ct);
        if (outcome == FavoriteOutcome.Unavailable) return NotFound();
        return CommonValidation.IsLocalReturnUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(ToolsController.Index), "Tools");
    }
}
