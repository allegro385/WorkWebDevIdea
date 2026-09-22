using SalesSupport.Common.Configuration;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>共通設定ファイルの読込み条件、相対パスの解決、および接続文字列の取得条件を確認します。</summary>
public sealed class CommonConfigurationTests : IDisposable
{
    private readonly string folderName = "ss-config-" + Guid.NewGuid().ToString("N");
    private readonly string folder;

    /// <summary>アプリの実行フォルダー配下へ、相対パスで指定できる検証用フォルダーを作成します。</summary>
    public CommonConfigurationTests()
    {
        folder = Path.Combine(AppContext.BaseDirectory, folderName);
        Directory.CreateDirectory(folder);
    }

    /// <summary>検証用フォルダーを削除します。</summary>
    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* 検証環境の残存は無視します。 */ }
    }

    /// <summary>パスの環境変数が未設定なら構成エラーとし、既定の場所を探しません。</summary>
    [Fact]
    public void LoadRequiresPathVariable() => WithPathVariable(null, () => Assert.Throws<ConfigurationException>(() => CommonConfiguration.Load()));

    /// <summary>環境変数の相対パスをアプリの実行フォルダーから解決し、接続文字列を取得します。</summary>
    [Fact]
    public void LoadResolvesPathVariableFromApplicationFolder()
    {
        var path = Write("settings.json", """{"ConnectionStrings":{"SalesSupport":"Data Source=verify;"}}""");
        WithPathVariable(path, () =>
        {
            var configuration = CommonConfiguration.Load();
            Assert.Equal("Data Source=verify;", new ConnectionStringProvider(configuration.Values).SalesSupportDatabase);
            Assert.Equal(Path.GetFullPath(folder), configuration.BaseDirectory);
        });
    }

    /// <summary>未設定・空白の指定を受け付けません。</summary>
    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public void LoadRejectsUnusablePath(string? path) => Assert.Throws<ConfigurationException>(() => CommonConfiguration.Load(path));

    /// <summary>実在するファイルでも絶対パスの指定は受け付けません。</summary>
    [Fact]
    public void LoadRejectsAbsolutePath()
    {
        Write("absolute.json", "{}");
        Assert.Throws<ConfigurationException>(() => CommonConfiguration.Load(Path.Combine(folder, "absolute.json")));
    }

    /// <summary>指定された相対パスにファイルがなければ構成エラーとします。</summary>
    [Fact]
    public void LoadRejectsMissingFile() => Assert.Throws<ConfigurationException>(() => CommonConfiguration.Load(folderName + "/absent.json"));

    /// <summary>書式不正の共通設定ファイルを構成エラーとして扱い、内容を例外へ含めません。</summary>
    [Fact]
    public void LoadRejectsMalformedFile()
    {
        var path = Write("broken.json", """{"ConnectionStrings":""");
        var exception = Assert.Throws<ConfigurationException>(() => CommonConfiguration.Load(path));
        Assert.Contains(CommonConfiguration.PathVariable, exception.Message);
    }

    /// <summary>接続文字列が未設定・空白なら起動を止めます。</summary>
    [Theory]
    [InlineData("""{"ConnectionStrings":{"SalesSupport":"   "}}""")]
    [InlineData("""{"ConnectionStrings":{}}""")]
    [InlineData("{}")]
    public void ProviderRejectsMissingConnectionString(string content)
    {
        var path = Write("empty.json", content);
        Assert.Throws<ConfigurationException>(() => new ConnectionStringProvider(CommonConfiguration.Load(path).Values));
    }

    /// <summary>設定内のフォルダーは共通設定ファイルのフォルダーを基準に解決します。</summary>
    [Fact]
    public void ResolvePathUsesConfigurationFileFolder()
    {
        var path = Write("paths.json", """{"SalesSupport":{"Storage":{"PermanentRoot":"../files"}}}""");
        var configuration = CommonConfiguration.Load(path);
        var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(folder, "../files")));
        Assert.Equal(expected, configuration.ResolvePath(configuration.Values["SalesSupport:Storage:PermanentRoot"], "Storage:PermanentRoot"));
    }

    /// <summary>設定内のフォルダーは未設定ならNULLとし、絶対パスの指定は受け付けません。</summary>
    [Fact]
    public void ResolvePathRejectsAbsolutePathAndKeepsUnsetAsNull()
    {
        var configuration = CommonConfiguration.Load(Write("paths.json", "{}"));
        Assert.Null(configuration.ResolvePath("   ", "Storage:TemporaryRoot"));
        Assert.Null(configuration.ResolvePath(null, "Storage:TemporaryRoot"));
        Assert.Throws<ConfigurationException>(() => configuration.ResolvePath(folder, "Storage:TemporaryRoot"));
    }

    /// <summary>アプリ固有の値は環境変数で与え、共通設定ファイルの値より優先します。</summary>
    [Fact]
    public void EnvironmentVariableOverridesFile()
    {
        var path = Write("tool.json", """{"SalesSupport":{"Application":{"ToolId":"SHARED"}}}""");
        var original = Environment.GetEnvironmentVariable("SalesSupport__Application__ToolId");
        Environment.SetEnvironmentVariable("SalesSupport__Application__ToolId", "T001");
        try { Assert.Equal("T001", CommonConfiguration.Load(path).Values["SalesSupport:Application:ToolId"]); }
        finally { Environment.SetEnvironmentVariable("SalesSupport__Application__ToolId", original); }
    }

    /// <summary>検証用の設定ファイルを作成し、アプリの実行フォルダーからの相対パスを返します。</summary>
    /// <param name="name">作成するファイル名。</param>
    /// <param name="content">UTF-8で書き込む内容。</param>
    /// <returns>実行フォルダーからの相対パス。</returns>
    private string Write(string name, string content)
    {
        File.WriteAllText(Path.Combine(folder, name), content);
        return folderName + "/" + name;
    }

    /// <summary>パスの環境変数を差し替えて検証し、元の値へ戻します。</summary>
    /// <param name="path">検証中に設定する値。NULLは未設定を表します。</param>
    /// <param name="verify">環境変数を差し替えた状態で実行する検証。</param>
    private static void WithPathVariable(string? path, Action verify)
    {
        var original = Environment.GetEnvironmentVariable(CommonConfiguration.PathVariable);
        Environment.SetEnvironmentVariable(CommonConfiguration.PathVariable, path);
        try { verify(); }
        finally { Environment.SetEnvironmentVariable(CommonConfiguration.PathVariable, original); }
    }
}
