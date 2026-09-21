using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Contracts;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Controllers;

/// <summary>Portalと各ツールが共有する現在ユーザー・通知設定のAPIです。</summary>
/// <remarks>自分のユーザーIDは認証情報から取得し、要求本文の任意のUserIdは受け付けません。</remarks>
[ApiController]
[Route("api/users/me")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UsersApiController(ICurrentUserAccessor current, IPreferenceService preferences) : ControllerBase
{
    /// <summary>検証済みの現在ユーザーを返します。SecurityStamp等は含めません。</summary>
    [HttpGet("")]
    public ActionResult<CurrentUserResponse> Me()
    {
        if (current.User is not { } user) return Unauthorized();
        return new CurrentUserResponse(user.UserId, user.DisplayName, user.RoleCode);
    }

    /// <summary>本人の通知設定を返します。未登録は整合性エラーとして503を返します。</summary>
    [HttpGet("preferences")]
    public async Task<ActionResult<UserPreferencesDto>> GetPreferences(CancellationToken ct)
    {
        if (current.User is not { } user) return Unauthorized();
        var preference = await preferences.GetAsync(user.UserId, ct);
        if (preference is null) return Unavailable();
        return preference;
    }

    /// <summary>本人の通知設定を更新します。CSRFトークンを必須とし、成功時は保存後の値を返します。</summary>
    [HttpPut("preferences")]
    public async Task<ActionResult<UserPreferencesDto>> PutPreferences([FromBody] UserPreferencesDto input, CancellationToken ct)
    {
        if (current.User is not { } user) return Unauthorized();
        var result = await preferences.SaveAsync(user.UserId, input, ct);
        if (result.Outcome == PreferenceOutcome.Succeeded && result.Preferences is { } saved) return saved;
        if (result.Outcome == PreferenceOutcome.Conflict)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "更新が競合しました。",
                detail: "他の操作で更新されています。最新の内容を取得してからやり直してください。");
        return Unavailable();
    }

    /// <summary>依存先の取得不能を許可側へ倒さず、安全な文言で返します。</summary>
    private ObjectResult Unavailable() => Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "設定を取得できませんでした。", detail: "時間をおいて、もう一度お試しください。");
}
