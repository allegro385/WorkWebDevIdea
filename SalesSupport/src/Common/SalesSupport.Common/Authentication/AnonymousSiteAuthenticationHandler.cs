using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.Authentication;

/// <summary>匿名モードで全要求の利用者として扱う、固定の共通ユーザーです。</summary>
/// <remarks>AspNetUsersへ登録せず、ログの識別値と権限判定のロールとしてだけ使用します。</remarks>
public static class AnonymousSiteUser
{
    /// <summary>ログへ記録する固定の内部ユーザーIDです。ログの3テーブルは外部キーを持たないため、ユーザー行を必要としません。</summary>
    public static readonly Guid UserId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>画面・ログ上の表示名です。個人を表しません。</summary>
    public const string DisplayName = "共通ユーザー";

    /// <summary>利用できるツールを決める一般ロールです。ToolRolesの割当てで公開範囲を調整します。</summary>
    public const string RoleCode = "A";
}

/// <summary>匿名モードの認証方式です。Cookieを読まず、固定の共通ユーザーを検証済み利用者として確定します。</summary>
/// <remarks>Commonが共通設定ファイルの認証モードを検証した場合だけ登録します。サイト・ツールの状態とロール割当ては通常どおりDBで判定します。</remarks>
public sealed class AnonymousSiteAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly CurrentUserAccessor current;

    /// <summary>匿名モードの認証方式名です。</summary>
    public const string SchemeName = "SalesSupport.AnonymousSite";

    /// <summary>標準の認証ハンドラーへ設定を渡し、検証済み利用者の確定先を受け取ります。</summary>
    /// <param name="options">認証方式の設定です。</param>
    /// <param name="logger">認証処理のログです。</param>
    /// <param name="encoder">URLのエンコーダーです。</param>
    /// <param name="current">同じ要求内の検証済み利用者を保持する領域です。</param>
    public AnonymousSiteAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        CurrentUserAccessor current)
        : base(options, logger, encoder) => this.current = current;

    /// <summary>固定の共通ユーザーの認証チケットを返します。要求のCookie・ヘッダーは参照しません。</summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, AnonymousSiteUser.UserId.ToString()),
            new Claim(ClaimTypes.Name, AnonymousSiteUser.DisplayName)
        ], SchemeName);
        current.SetVerified(new CurrentUser(AnonymousSiteUser.UserId, AnonymousSiteUser.DisplayName, AnonymousSiteUser.RoleCode));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
