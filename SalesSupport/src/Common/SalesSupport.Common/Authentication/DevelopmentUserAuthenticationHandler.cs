using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.Authentication;

/// <summary>認証を省略したデバッグ実行で使う、DBに登録しない開発用の仮管理者です。</summary>
public static class DevelopmentUser
{
    /// <summary>仮管理者の内部UserIdです。未設定を表す<see cref="Guid.Empty"/>と区別します。</summary>
    public static readonly Guid UserId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>仮管理者のログインIDです。表示名にも使用します。</summary>
    public const string LoginId = "AdminUser";

    /// <summary>仮管理者のロールです。</summary>
    public const string RoleCode = "ADMIN";

    /// <summary>現行ユーザーとして設定する仮管理者を作成します。</summary>
    /// <returns>固定値の仮管理者です。</returns>
    public static CurrentUser Create() => new(UserId, LoginId, RoleCode);
}

/// <summary>認証を省略したデバッグ実行で、Cookieを読まずに仮管理者を要求ごとの認証済みユーザーとします。</summary>
/// <remarks>Commonが開発環境でのデバッグ実行を検証した場合だけ登録します。認証チケット・Cookieは発行しません。</remarks>
public sealed class DevelopmentUserAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>開発用の認証方式名です。</summary>
    public const string SchemeName = "SalesSupport.DevelopmentUser";

    private readonly CurrentUserAccessor current;

    /// <summary>標準の認証ハンドラーへ設定を渡し、同じ要求の現行ユーザーを受け取ります。</summary>
    /// <param name="options">認証方式の設定です。</param>
    /// <param name="logger">認証処理のログです。</param>
    /// <param name="encoder">URLのエンコーダーです。</param>
    /// <param name="current">仮管理者を設定する要求スコープの現行ユーザーです。</param>
    public DevelopmentUserAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, CurrentUserAccessor current)
        : base(options, logger, encoder) => this.current = current;

    /// <summary>仮管理者を現行ユーザーへ設定し、認証済みとして返します。</summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = DevelopmentUser.Create();
        current.SetVerified(user);
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()), new Claim(ClaimTypes.Name, DevelopmentUser.LoginId), new Claim(ClaimTypes.Role, user.RoleCode)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
