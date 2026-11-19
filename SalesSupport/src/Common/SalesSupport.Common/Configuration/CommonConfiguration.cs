using Microsoft.Extensions.Configuration;
using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.Configuration;

/// <summary>Commonが所有する共通設定ファイルの内容と、設定内の相対パスを解決する基準フォルダーです。</summary>
public sealed class CommonConfiguration
{
    /// <summary>共通設定ファイルの場所を与えるホスト設定キーです。環境変数では<c>SalesSupport__CommonConfigPath</c>です。</summary>
    public const string PathKey = "SalesSupport:CommonConfigPath";

    /// <summary>標準配置（Portalは<c>&lt;ルート&gt;/apps/&lt;フォルダー名&gt;/</c>、Webツールは<c>&lt;ルート&gt;/apps/tools/&lt;ToolId&gt;/</c>、共通設定は<c>&lt;ルート&gt;/config/</c>）での、コンテンツルートからの既定の相対パスを返します。</summary>
    /// <param name="kind">読み込むアプリの種別です。</param>
    /// <returns>Portalは<c>../../config/salessupport.common.json</c>、Webツールは<c>../../../config/salessupport.common.json</c>です。</returns>
    public static string DefaultPath(ApplicationKind kind) => kind == ApplicationKind.Tool ? "../../../config/salessupport.common.json" : "../../config/salessupport.common.json";

    /// <summary>読み込み済みの設定と基準フォルダーを保持します。生成は<see cref="Load"/>から行います。</summary>
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

    /// <summary>コンテンツルートからの相対パスで共通設定ファイルを読み込みます。親フォルダーは探しません。</summary>
    /// <param name="contentRoot">アプリのコンテンツルートの絶対パス。IISでは配置フォルダー、開発時はプロジェクトフォルダーです。</param>
    /// <param name="path">コンテンツルートからの相対パス。NULL・空白はアプリ種別ごとの<see cref="DefaultPath"/>を使います。</param>
    /// <param name="kind">既定の相対パスを決めるアプリの種別です。</param>
    /// <returns>共通設定ファイルの内容と基準フォルダー。</returns>
    public static CommonConfiguration Load(string contentRoot, string? path, ApplicationKind kind)
    {
        var file = Combine(contentRoot, string.IsNullOrWhiteSpace(path) ? DefaultPath(kind) : path, PathKey);
        // パスは秘密値ではないため、置き場所を確認できるよう解決後のパスを示します。
        if (!File.Exists(file)) throw new ConfigurationException(PathKey, $"ファイルがありません: {file}");
        try
        {
            // アプリ固有の値と配置ごとの上書きは環境変数で与えるため、ファイルより後に環境変数を適用します。
            var values = new ConfigurationBuilder().AddJsonFile(file, optional: false, reloadOnChange: false).AddEnvironmentVariables().Build();
            return new CommonConfiguration(values, Path.GetDirectoryName(file)!);
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException)
        {
            // 書式不正の詳細は設定値を含み得るため、ファイルの場所だけを通知します。
            throw new ConfigurationException(PathKey, $"JSONの書式が不正です: {file}");
        }
    }

    /// <summary>設定されたフォルダー・ファイルの相対パスを共通設定ファイルのフォルダーから解決します。</summary>
    /// <param name="value">設定値。NULL・空白は未設定としてNULLを返します。</param>
    /// <param name="key">構成エラーへ表示する設定キー。</param>
    /// <returns>解決した絶対パス。未設定ならNULL。</returns>
    public string? ResolvePath(string? value, string key) => string.IsNullOrWhiteSpace(value) ? null : Combine(BaseDirectory, value, key);

    /// <summary>基準フォルダーと相対パスを結合します。絶対パスおよび制御文字を含む指定は構成エラーとします。</summary>
    /// <param name="baseDirectory">解決の基準にする絶対パス。</param>
    /// <param name="path">基準フォルダーからの相対パス。</param>
    /// <param name="key">構成エラーへ表示する設定キー。</param>
    /// <returns>解決した絶対パス。</returns>
    private static string Combine(string baseDirectory, string path, string key)
    {
        if (Path.IsPathRooted(path) || path.Any(char.IsControl)) throw new ConfigurationException(key);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path, baseDirectory));
    }
}
