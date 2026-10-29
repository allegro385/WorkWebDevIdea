using Microsoft.Extensions.Configuration;

namespace SalesSupport.Common.Configuration;

/// <summary>サイト全体の認証方式です。</summary>
public enum SiteAuthenticationMode
{
    /// <summary>メールOTPと共有Cookieで本人を認証する通常のモードです。未設定時の既定です。</summary>
    EmailOtp,

    /// <summary>ログインを行わず、固定の共通ユーザーとして利用させる段階リリース第1段のモードです。</summary>
    Anonymous
}

/// <summary>共通設定ファイルからサイトの認証モードを確定します。</summary>
/// <remarks>Portalと全ツールが同じ共通設定ファイルを読むため、この1か所の値で全アプリの認証方式が決まります。</remarks>
public static class SiteAuthenticationModeSettings
{
    /// <summary>認証モードの設定キーです。環境変数では<c>SalesSupport__Site__AuthenticationMode</c>です。</summary>
    public const string Key = "SalesSupport:Site:AuthenticationMode";

    /// <summary>未設定はメールOTPとし、定義済みの値以外は構成エラーとして起動を止めます。</summary>
    /// <param name="configuration">共通設定ファイルと配置環境変数から構成した設定です。</param>
    /// <returns>検証済みの認証モードです。</returns>
    public static SiteAuthenticationMode Read(IConfiguration configuration)
    {
        var value = configuration[Key];
        if (value is null || value == nameof(SiteAuthenticationMode.EmailOtp)) return SiteAuthenticationMode.EmailOtp;
        // 大文字小文字の揺れや真偽値を受け付けず、設定の書き間違いを匿名側へ倒しません。
        if (value == nameof(SiteAuthenticationMode.Anonymous)) return SiteAuthenticationMode.Anonymous;
        throw new ConfigurationException(Key);
    }
}
