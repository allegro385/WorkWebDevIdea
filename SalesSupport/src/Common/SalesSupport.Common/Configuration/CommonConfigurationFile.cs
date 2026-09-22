using Microsoft.Extensions.Configuration;

namespace SalesSupport.Common.Configuration;

/// <summary>Commonが所有する共通設定ファイルを読み込みます。接続文字列を含む共通設定の取得元をCommonへ一本化します。</summary>
public static class CommonConfigurationFile
{
    /// <summary>共通設定ファイルの絶対パスを与える環境変数名です。Portalと各ツールへ同じファイルを指定します。</summary>
    public const string PathVariable = "SalesSupport__CommonConfigPath";

    /// <summary>環境変数が指す共通設定ファイルから設定を構成します。</summary>
    /// <returns>共通設定ファイルを基礎とし、配置環境変数で上書きした設定です。</returns>
    public static IConfigurationRoot Load() => Load(Environment.GetEnvironmentVariable(PathVariable));

    /// <summary>指定した共通設定ファイルから設定を構成します。未設定、相対パス、不在および書式不正は構成エラーとします。</summary>
    /// <param name="path">共通設定ファイルの絶対パス。NULL・空白は未設定として扱います。</param>
    /// <returns>共通設定ファイルを基礎とし、配置環境変数で上書きした設定です。</returns>
    public static IConfigurationRoot Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new ConfigurationException(PathVariable);
        try
        {
            // アプリ固有の値と配置ごとの上書きは環境変数で与えるため、ファイルより後に環境変数を適用します。
            return new ConfigurationBuilder().AddJsonFile(path, optional: false, reloadOnChange: false).AddEnvironmentVariables().Build();
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException)
        {
            // 書式不正の詳細は設定値を含み得るため、環境変数名だけを通知します。
            throw new ConfigurationException(PathVariable);
        }
    }
}
