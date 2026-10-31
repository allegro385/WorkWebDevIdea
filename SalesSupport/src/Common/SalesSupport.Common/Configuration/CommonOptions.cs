using Microsoft.AspNetCore.Http;
using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.Configuration;

/// <summary>ホストから共通基盤へ渡す非秘密の設定です。</summary>
public sealed class CommonOptions
{
    public ApplicationKind Kind { get; set; }
    public string EnvironmentCode { get; set; } = "";

    /// <summary>共通ヘッダー・フッターとメール文面で使うサイト名です。全アプリで同じ値です。</summary>
    public string SiteName { get; set; } = "";

    /// <summary>このアプリ自身の名前です。Portalはポータル名、各ツールはそのツール名で、サイト名とは別に持ちます。</summary>
    public string ApplicationName { get; set; } = "";
    public string? ToolId { get; set; }
    public string PortalBaseUrl { get; set; } = "";

    /// <summary>ツールの起動モードです。Portalでは常に<see cref="ToolExecutionMode.Portal"/>です。</summary>
    public ToolExecutionMode ExecutionMode { get; set; }

    /// <summary>共通設定ファイルで選んだサイトの認証モードです。Portalと全ツールで同じ値です。</summary>
    public SiteAuthenticationMode AuthenticationMode { get; set; }

    /// <summary>共通設定ファイルで選んだサイトの通信方式です。Portalと全ツールで同じ値です。</summary>
    public SiteTransportMode TransportMode { get; set; }

    /// <summary>起動時に検証された、Portal・DB・共通設定ファイルを使わない単独開発のツールかを返します。</summary>
    public bool IsStandalone => Kind == ApplicationKind.Tool && IsDevelopment && ExecutionMode == ToolExecutionMode.Standalone;

    /// <summary>ログインを行わず、固定の共通ユーザーとして利用させる匿名モードかを返します。</summary>
    /// <remarks>共通設定ファイルを読まない単独開発は対象外です。</remarks>
    public bool IsAnonymous => !IsStandalone && AuthenticationMode == SiteAuthenticationMode.Anonymous;

    /// <summary>本人を特定できる利用者がいるかを返します。本人向けデータの所有者を決められるかの判定に使います。</summary>
    /// <remarks>単独開発には認証ユーザーがなく、匿名モードの固定共通ユーザーはAspNetUsersに存在しないため、どちらもfalseです。</remarks>
    public bool HasIdentifiedUser => !IsStandalone && !IsAnonymous;

    /// <summary>HTTPSのバインドが未完了の期間に使う、HTTPで公開するモードかを返します。</summary>
    /// <remarks>共通設定ファイルを読まない単独開発は対象外です。</remarks>
    public bool IsHttpTransport => !IsStandalone && TransportMode == SiteTransportMode.Http;

    /// <summary>Cookieへ付けるSecure属性の条件です。HTTP運用だけ、要求がHTTPSの場合に付けます。</summary>
    public CookieSecurePolicy CookieSecurePolicy => IsHttpTransport ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

    /// <summary>この通信方式で受け付けるURLのスキームです。PortalのURLの検証に使います。</summary>
    public string UrlScheme => IsHttpTransport ? Uri.UriSchemeHttp : Uri.UriSchemeHttps;

    /// <summary>開発環境向けの動作を適用するかを判定します。未設定・不正値は開発扱いにしません。</summary>
    public bool IsDevelopment => EnvironmentCode == "DEVELOPMENT";
}

/// <summary>ファイル保存領域の設定です。未設定の領域は利用できません。</summary>
public sealed class StorageOptions
{
    /// <summary>一時ファイルの絶対パスです。未設定なら一時保存を提供しません。</summary>
    public string? TemporaryRoot { get; set; }
    /// <summary>永続ファイルの絶対パスです。未設定なら永続保存を提供しません。</summary>
    public string? PermanentRoot { get; set; }
    /// <summary>清掃処理だけに適用する待機上限（秒）です。</summary>
    public int CleanupTimeoutSeconds { get; set; } = 5;
}

/// <summary>SMTPの接続方式です。暗号化なしの送信は選択できません。</summary>
public enum MailTlsMode { StartTls, SslOnConnect }

/// <summary>SMTP接続と開発時の宛先置換の設定です。</summary>
public sealed class MailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public MailTlsMode? TlsMode { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "";
    public string? ReplyTo { get; set; }
    /// <summary>開発環境で実宛先を置き換える唯一の宛先です。</summary>
    public string? DevelopmentRecipient { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    /// <summary>1通あたりの宛先上限です。メール有効時は必須です。</summary>
    public int? MaxRecipients { get; set; }
}

/// <summary>外部HTTP呼出しの既定条件です。</summary>
public sealed class HttpOptions
{
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>転送ヘッダーを信頼するプロキシの設定です。</summary>
public sealed class ProxyOptions
{
    /// <summary>信頼するプロキシのIPアドレスです。空の場合は転送ヘッダーを採用しません。</summary>
    public string[] KnownProxies { get; set; } = [];
}
