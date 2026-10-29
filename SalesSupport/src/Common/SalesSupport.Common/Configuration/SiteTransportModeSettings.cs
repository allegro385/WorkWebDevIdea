using Microsoft.Extensions.Configuration;

namespace SalesSupport.Common.Configuration;

/// <summary>サイト全体の通信方式です。</summary>
public enum SiteTransportMode
{
    /// <summary>HTTPSで公開する通常のモードです。未設定時の既定です。</summary>
    Https,

    /// <summary>HTTPSのバインドと通信許可が未完了の期間だけ使う、HTTPで公開するモードです。</summary>
    Http
}

/// <summary>共通設定ファイルからサイトの通信方式を確定します。</summary>
/// <remarks>Portalと全ツールが同じ共通設定ファイルを読むため、この1か所の値で全アプリの通信方式が決まります。</remarks>
public static class SiteTransportModeSettings
{
    /// <summary>通信方式の設定キーです。環境変数では<c>SalesSupport__Site__TransportMode</c>です。</summary>
    public const string Key = "SalesSupport:Site:TransportMode";

    /// <summary>未設定はHTTPSとし、定義済みの値以外は構成エラーとして起動を止めます。</summary>
    /// <param name="configuration">共通設定ファイルと配置環境変数から構成した設定です。</param>
    /// <returns>検証済みの通信方式です。</returns>
    public static SiteTransportMode Read(IConfiguration configuration)
    {
        var value = configuration[Key];
        if (value is null || value == nameof(SiteTransportMode.Https)) return SiteTransportMode.Https;
        // 大文字小文字の揺れや真偽値を受け付けず、設定の書き間違いをHTTP側へ倒しません。
        if (value == nameof(SiteTransportMode.Http)) return SiteTransportMode.Http;
        throw new ConfigurationException(Key);
    }
}
