using SalesSupport.Common.Configuration;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>共通設定ファイルの読込み条件、相対パスの解決、および接続文字列の取得条件を確認します。</summary>
[Collection(CommonConfigPathCollection.Name)]
public sealed class CommonConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ss-config-" + Guid.NewGuid().ToString("N"));
    private readonly string contentRoot;
    private readonly string configFolder;

    /// <summary>標準配置（<c>apps/&lt;アプリ名&gt;</c>と<c>config</c>）を模した検証用フォルダーを作成します。</summary>
    public CommonConfigurationTests()
    {
        contentRoot = Path.Combine(root, "apps", "T001");
        configFolder = Path.Combine(root, "config");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(configFolder);
    }

    /// <summary>検証用フォルダーを削除します。</summary>
    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { /* 検証環境の残存は無視します。 */ }
    }

    /// <summary>パスが未設定なら、コンテンツルートから既定の相対パスで標準配置の共通設定ファイルを読みます。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void LoadUsesDefaultPathFromContentRoot(string? path)
    {
        File.WriteAllText(Path.Combine(configFolder, "salessupport.common.json"), """{"ConnectionStrings":{"SalesSupport":"Data Source=verify;"}}""");
        var configuration = CommonConfiguration.Load(contentRoot, path);
        Assert.Equal("Data Source=verify;", new ConnectionStringProvider(configuration.Values).SalesSupportDatabase);
        Assert.Equal(configFolder, configuration.BaseDirectory);
        Assert.Equal("../../config/salessupport.common.json", CommonConfiguration.DefaultPath);
    }

    /// <summary>指定した相対パスはアプリの実行フォルダーではなくコンテンツルートから解決します。</summary>
    [Fact]
    public void LoadResolvesPathFromContentRoot()
    {
        var path = Write("local.json", """{"ConnectionStrings":{"SalesSupport":"Data Source=local;"}}""");
        var configuration = CommonConfiguration.Load(contentRoot, path);
        Assert.Equal("Data Source=local;", new ConnectionStringProvider(configuration.Values).SalesSupportDatabase);
        Assert.Equal(configFolder, configuration.BaseDirectory);
    }

    /// <summary>実在するファイルでも絶対パスの指定は受け付けません。</summary>
    [Fact]
    public void LoadRejectsAbsolutePath()
    {
        Write("absolute.json", "{}");
        Assert.Throws<ConfigurationException>(() => CommonConfiguration.Load(contentRoot, Path.Combine(configFolder, "absolute.json")));
    }

    /// <summary>ファイルがなければ構成エラーとし、利用者が置き場所を確認できるよう解決後のパスを示します。親フォルダーは探しません。</summary>
    [Fact]
    public void LoadRejectsMissingFileWithResolvedPath()
    {
        File.WriteAllText(Path.Combine(root, "salessupport.common.json"), "{}");
        var exception = Assert.Throws<ConfigurationException>(() => CommonConfiguration.Load(contentRoot, null));
        Assert.Contains(CommonConfiguration.PathKey, exception.Message);
        Assert.Contains(Path.Combine(configFolder, "salessupport.common.json"), exception.Message);
    }

    /// <summary>書式不正の共通設定ファイルを構成エラーとして扱い、内容を例外へ含めません。</summary>
    [Fact]
    public void LoadRejectsMalformedFile()
    {
        var path = Write("broken.json", """{"ConnectionStrings":{"SalesSupport":"Password=secret""");
        var exception = Assert.Throws<ConfigurationException>(() => CommonConfiguration.Load(contentRoot, path));
        Assert.Contains(CommonConfiguration.PathKey, exception.Message);
        Assert.Contains(Path.Combine(configFolder, "broken.json"), exception.Message);
        Assert.DoesNotContain("secret", exception.Message);
    }

    /// <summary>接続文字列が未設定・空白なら起動を止めます。</summary>
    [Theory]
    [InlineData("""{"ConnectionStrings":{"SalesSupport":"   "}}""")]
    [InlineData("""{"ConnectionStrings":{}}""")]
    [InlineData("{}")]
    public void ProviderRejectsMissingConnectionString(string content)
    {
        var path = Write("empty.json", content);
        Assert.Throws<ConfigurationException>(() => new ConnectionStringProvider(CommonConfiguration.Load(contentRoot, path).Values));
    }

    /// <summary>設定内のフォルダーは共通設定ファイルのフォルダーを基準に解決します。</summary>
    [Fact]
    public void ResolvePathUsesConfigurationFileFolder()
    {
        var path = Write("paths.json", """{"SalesSupport":{"Storage":{"PermanentRoot":"../files"}}}""");
        var configuration = CommonConfiguration.Load(contentRoot, path);
        var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(configFolder, "../files")));
        Assert.Equal(expected, configuration.ResolvePath(configuration.Values["SalesSupport:Storage:PermanentRoot"], "Storage:PermanentRoot"));
    }

    /// <summary>設定内のフォルダーは未設定ならNULLとし、絶対パスの指定は受け付けません。</summary>
    [Fact]
    public void ResolvePathRejectsAbsolutePathAndKeepsUnsetAsNull()
    {
        var configuration = CommonConfiguration.Load(contentRoot, Write("paths.json", "{}"));
        Assert.Null(configuration.ResolvePath("   ", "Storage:TemporaryRoot"));
        Assert.Null(configuration.ResolvePath(null, "Storage:TemporaryRoot"));
        Assert.Throws<ConfigurationException>(() => configuration.ResolvePath(configFolder, "Storage:TemporaryRoot"));
    }

    /// <summary>アプリ固有の値は環境変数で与え、共通設定ファイルの値より優先します。</summary>
    [Fact]
    public void EnvironmentVariableOverridesFile()
    {
        var path = Write("tool.json", """{"SalesSupport":{"Application":{"ToolId":"SHARED"}}}""");
        var original = Environment.GetEnvironmentVariable("SalesSupport__Application__ToolId");
        Environment.SetEnvironmentVariable("SalesSupport__Application__ToolId", "T001");
        try { Assert.Equal("T001", CommonConfiguration.Load(contentRoot, path).Values["SalesSupport:Application:ToolId"]); }
        finally { Environment.SetEnvironmentVariable("SalesSupport__Application__ToolId", original); }
    }

    /// <summary>検証用の設定ファイルを<c>config</c>へ作成し、コンテンツルートからの相対パスを返します。</summary>
    /// <param name="name">作成するファイル名。</param>
    /// <param name="content">UTF-8で書き込む内容。</param>
    /// <returns>コンテンツルートからの相対パス。</returns>
    private string Write(string name, string content)
    {
        File.WriteAllText(Path.Combine(configFolder, name), content);
        return "../../config/" + name;
    }
}

/// <summary>プロセス全体の環境変数を差し替える検証と、共通設定を読み込む検証を並列に実行しないためのグループです。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CommonConfigPathCollection
{
    /// <summary>グループ名です。</summary>
    public const string Name = "CommonConfigPath";
}
