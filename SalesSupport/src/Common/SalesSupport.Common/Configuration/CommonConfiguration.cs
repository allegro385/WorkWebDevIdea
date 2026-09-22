using Microsoft.Extensions.Configuration;

namespace SalesSupport.Common.Configuration;

/// <summary>Commonが所有する共通設定ファイルの内容と、設定内の相対パスを解決する基準フォルダーです。</summary>
public sealed class CommonConfiguration
{
    /// <summary>共通設定ファイルの場所を与える環境変数名です。値はアプリの実行フォルダーからの相対パスです。</summary>
    public const string PathVariable = "SalesSupport__CommonConfigPath";

    /// <summary>読み込み済みの設定と基準フォルダーを保持します。生成は<see cref="Load()"/>から行います。</summary>
    /// <param name="values">共通設定ファイルと配置環境変数から構成した設定。</param>
    /// <param name="baseDirectory">共通設定ファイルがあるフォルダーの絶対パス。</param>
    private CommonConfiguration(IConfigurationRoot values, string baseDirectory)
    {
        Values = values;
        BaseDirectory = baseDirectory;
    }

    /// <summary>共通設定ファイルと配置環境変数から構成した設定です。</summary>
    public IConfigurationRoot Values { get; }

    /// <summary>共通設定ファイルがあるフォルダーの絶対パスです。設定内の相対パスはこのフォルダーを基準に解決します。</summary>
    public string BaseDirectory { get; }

    /// <summary>環境変数が指す共通設定ファイルを読み込みます。</summary>
    /// <returns>共通設定ファイルの内容と基準フォルダー。</returns>
    public static CommonConfiguration Load() => Load(Environment.GetEnvironmentVariable(PathVariable));

    /// <summary>指定した共通設定ファイルを読み込みます。未設定、絶対パス、不在および書式不正は構成エラーとします。</summary>
    /// <param name="path">アプリの実行フォルダーからの相対パス。NULL・空白は未設定として扱います。</param>
    /// <returns>共通設定ファイルの内容と基準フォルダー。</returns>
    public static CommonConfiguration Load(string? path)
    {
        var file = Combine(AppContext.BaseDirectory, path, PathVariable);
        if (!File.Exists(file)) throw new ConfigurationException(PathVariable);
        var directory = Path.GetDirectoryName(file) ?? throw new ConfigurationException(PathVariable);
        try
        {
            // アプリ固有の値と配置ごとの上書きは環境変数で与えるため、ファイルより後に環境変数を適用します。
            var values = new ConfigurationBuilder().AddJsonFile(file, optional: false, reloadOnChange: false).AddEnvironmentVariables().Build();
            return new CommonConfiguration(values, directory);
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException)
        {
            // 書式不正の詳細は設定値を含み得るため、環境変数名だけを通知します。
            throw new ConfigurationException(PathVariable);
        }
    }

    /// <summary>設定されたフォルダー・ファイルの相対パスを共通設定ファイルのフォルダーから解決します。</summary>
    /// <param name="value">設定値。NULL・空白は未設定としてNULLを返します。</param>
    /// <param name="key">構成エラーへ表示する設定キー。</param>
    /// <returns>解決した絶対パス。未設定ならNULL。</returns>
    public string? ResolvePath(string? value, string key) => string.IsNullOrWhiteSpace(value) ? null : Combine(BaseDirectory, value, key);

    /// <summary>基準フォルダーと相対パスを結合します。未設定、絶対パスおよび制御文字を含む指定は構成エラーとします。</summary>
    /// <param name="baseDirectory">解決の基準にする絶対パス。</param>
    /// <param name="path">基準フォルダーからの相対パス。</param>
    /// <param name="key">構成エラーへ表示する設定キーまたは環境変数名。</param>
    /// <returns>解決した絶対パス。</returns>
    private static string Combine(string baseDirectory, string? path, string key)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Any(char.IsControl)) throw new ConfigurationException(key);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path, baseDirectory));
    }
}
