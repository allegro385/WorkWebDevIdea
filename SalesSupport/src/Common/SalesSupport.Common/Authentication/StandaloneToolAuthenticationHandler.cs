using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SalesSupport.Common.Authentication;

/// <summary>単独開発ではCookieを読まず、認証済みユーザーや認証チケットを生成しません。</summary>
public sealed class StandaloneToolAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SalesSupport.StandaloneTool";

    /// <summary>標準の認証ハンドラーへ要求スコープの設定を渡します。</summary>
    public StandaloneToolAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    /// <summary>認証なしであることを返します。アクセス許可は単独開発専用のツール認可に限定します。</summary>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
