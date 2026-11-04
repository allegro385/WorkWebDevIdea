using Microsoft.Extensions.Configuration;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>共通設定ファイルの雛形が、コメント付きのままCommonの読込みで使えることを確認します。</summary>
public sealed class ConfigExampleTests
{
    /// <summary>コメントが読込みを妨げず、通信方式と認証モードをSiteの下で読めます。</summary>
    /// <param name="fileName">configフォルダーの雛形のファイル名です。</param>
    [Theory]
    [InlineData("salessupport.common.example.json")]
    [InlineData("salessupport.common.Development.example.json")]
    public void ExampleFilesLoadWithComments(string fileName)
    {
        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../config"));

        var values = new ConfigurationBuilder().AddJsonFile(Path.Combine(folder, fileName), optional: false).Build();

        Assert.Equal("EmailOtp", values["SalesSupport:Site:AuthenticationMode"]);
        Assert.Equal("Https", values["SalesSupport:Site:TransportMode"]);
        Assert.Null(values["SalesSupport:TransportMode"]);
    }
}
