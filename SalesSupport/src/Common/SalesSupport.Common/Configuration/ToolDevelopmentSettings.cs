using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.Configuration;

/// <summary>ツールのデバッグ実行で使う確認の省略設定です。</summary>
/// <param name="RequireAuthentication">共有Cookieで認証します。falseは開発用の仮管理者で動作します。</param>
/// <param name="UseToolMaster">ツールマスタを参照し、DBログを保存します。</param>
public sealed record ToolDevelopmentSwitches(bool RequireAuthentication, bool UseToolMaster);

/// <summary>認証・ツールマスタの省略を、明示的な開発環境でのデバッグ実行に限定して読み取ります。</summary>
public static class ToolDevelopmentSettings
{
    /// <summary>共有Cookie認証を行うかの設定キーです。</summary>
    public const string RequireAuthenticationKey = "SalesSupport:Tool:RequireAuthentication";

    /// <summary>ツールマスタを参照するかの設定キーです。</summary>
    public const string UseToolMasterKey = "SalesSupport:Tool:UseToolMaster";

    /// <summary>既定は両方trueとし、falseはCommonとホストの両方が開発環境の場合だけ受け付けます。Portalには適用しません。</summary>
    /// <param name="configuration">共通設定ファイルと環境変数から構成した設定です。</param>
    /// <param name="kind">PortalまたはToolです。</param>
    /// <param name="environment">ホストの実行環境です。NULLの場合は省略を認めません。</param>
    /// <returns>検証済みの省略設定です。</returns>
    public static ToolDevelopmentSwitches Read(IConfiguration configuration, ApplicationKind kind, IHostEnvironment? environment)
    {
        if (kind != ApplicationKind.Tool) return new(true, true);
        var development = configuration["Portal:EnvironmentCode"] == "DEVELOPMENT" && environment?.IsDevelopment() == true;
        return new(ReadFlag(configuration, RequireAuthenticationKey, development), ReadFlag(configuration, UseToolMasterKey, development));
    }

    /// <summary>未設定をtrueとし、不正値と開発環境以外でのfalseを構成エラーにします。</summary>
    /// <param name="configuration">読み取り元の設定です。</param>
    /// <param name="key">設定キーです。</param>
    /// <param name="development">Commonとホストの両方が開発環境かです。</param>
    /// <returns>設定値です。</returns>
    private static bool ReadFlag(IConfiguration configuration, string key, bool development)
    {
        var value = configuration[key];
        if (value is null) return true;
        if (!bool.TryParse(value, out var flag) || !flag && !development) throw new ConfigurationException(key);
        return flag;
    }
}
