using SalesSupport.Common.Configuration;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>共通設定ファイルの読込み条件と、Commonが供給する接続文字列の取得条件を確認します。</summary>
public sealed class CommonConfigurationTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("ss-config-").FullName;

    /// <summary>検証用の一時ディレクトリを削除します。</summary>
    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* 検証環境の残存は無視します。 */ }
    }

    /// <summary>パスの環境変数が未設定なら構成エラーとし、既定の場所を探しません。</summary>
    [Fact]
    public void LoadRequiresPathVariable() => WithPathVariable(null, () => Assert.Throws<ConfigurationException>(() => CommonConfigurationFile.Load()));

    /// <summary>環境変数が指す共通設定ファイルから接続文字列を取得します。</summary>
    [Fact]
    public void LoadReadsFileFromPathVariable()
    {
        var path = Write("settings.json", """{"ConnectionStrings":{"SalesSupport":"Data Source=verify;"}}""");
        WithPathVariable(path, () =>
        {
            var provider = new ConnectionStringProvider(CommonConfigurationFile.Load());
            Assert.Equal("Data Source=verify;", provider.SalesSupportDatabase);
        });
    }

    /// <summary>相対パス・不在ファイル・空白指定を受け付けません。</summary>
    [Theory]
    [InlineData("config/salessupport.common.json")]
    [InlineData("   ")]
    [InlineData(null)]
    public void LoadRejectsUnusablePath(string? path) => Assert.Throws<ConfigurationException>(() => CommonConfigurationFile.Load(path));

    /// <summary>指定された絶対パスにファイルがなければ構成エラーとします。</summary>
    [Fact]
    public void LoadRejectsMissingFile() => Assert.Throws<ConfigurationException>(() => CommonConfigurationFile.Load(Path.Combine(directory, "absent.json")));

    /// <summary>書式不正の共通設定ファイルを構成エラーとして扱い、内容を例外へ含めません。</summary>
    [Fact]
    public void LoadRejectsMalformedFile()
    {
        var path = Write("broken.json", """{"ConnectionStrings":""");
        var exception = Assert.Throws<ConfigurationException>(() => CommonConfigurationFile.Load(path));
        Assert.Contains(CommonConfigurationFile.PathVariable, exception.Message);
    }

    /// <summary>接続文字列が未設定・空白なら起動を止めます。</summary>
    [Theory]
    [InlineData("""{"ConnectionStrings":{"SalesSupport":"   "}}""")]
    [InlineData("""{"ConnectionStrings":{}}""")]
    [InlineData("{}")]
    public void ProviderRejectsMissingConnectionString(string content)
    {
        var path = Write("empty.json", content);
        Assert.Throws<ConfigurationException>(() => new ConnectionStringProvider(CommonConfigurationFile.Load(path)));
    }

    /// <summary>アプリ固有の値は環境変数で与え、共通設定ファイルの値より優先します。</summary>
    [Fact]
    public void EnvironmentVariableOverridesFile()
    {
        var path = Write("tool.json", """{"SalesSupport":{"Application":{"ToolId":"SHARED"}}}""");
        var original = Environment.GetEnvironmentVariable("SalesSupport__Application__ToolId");
        Environment.SetEnvironmentVariable("SalesSupport__Application__ToolId", "T001");
        try { Assert.Equal("T001", CommonConfigurationFile.Load(path)["SalesSupport:Application:ToolId"]); }
        finally { Environment.SetEnvironmentVariable("SalesSupport__Application__ToolId", original); }
    }

    /// <summary>検証用の設定ファイルを一時ディレクトリへ作成し、絶対パスを返します。</summary>
    /// <param name="name">作成するファイル名。</param>
    /// <param name="content">UTF-8で書き込む内容。</param>
    private string Write(string name, string content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>パスの環境変数を差し替えて検証し、元の値へ戻します。</summary>
    /// <param name="path">検証中に設定する値。NULLは未設定を表します。</param>
    /// <param name="verify">環境変数を差し替えた状態で実行する検証。</param>
    private static void WithPathVariable(string? path, Action verify)
    {
        var original = Environment.GetEnvironmentVariable(CommonConfigurationFile.PathVariable);
        Environment.SetEnvironmentVariable(CommonConfigurationFile.PathVariable, path);
        try { verify(); }
        finally { Environment.SetEnvironmentVariable(CommonConfigurationFile.PathVariable, original); }
    }
}
