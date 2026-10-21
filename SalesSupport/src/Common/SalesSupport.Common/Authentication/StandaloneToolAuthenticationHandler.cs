using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SalesSupport.Common.Authentication;

/// <summary>単独開発の既定の認証方式です。Cookieを読まず、認証済みユーザー・仮ユーザー・認証チケットを作りません。</summary>
/// <remarks>Commonが開発環境での単独開発を検証した場合だけ登録します。利用の許可は対象ToolIdの<c>ToolUse</c>に限定した既定認可で行います。</remarks>
public sealed class StandaloneToolAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>単独開発の認証方式名です。</summary>
    public const string SchemeName = "SalesSupport.StandaloneTool";

    /// <summary>標準の認証ハンドラーへ設定を渡します。</summary>
    /// <param name="options">認証方式の設定です。</param>
    /// <param name="logger">認証処理のログです。</param>
    /// <param name="encoder">URLのエンコーダーです。</param>
    public StandaloneToolAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    /// <summary>認証なしであることを返します。</summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
