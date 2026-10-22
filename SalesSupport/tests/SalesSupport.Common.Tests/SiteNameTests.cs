using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DependencyInjection;
using SalesSupport.Common.Mail;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>サイト名を設定から受け取り、アプリ自身の名前と取り違えないことを確認します。</summary>
/// <remarks>共通設定ファイルを読まない単独開発で登録し、DB・Portalへは接続しません。</remarks>
public sealed class SiteNameTests
{
    /// <summary>設定のサイト名をそのまま共通設定として読み取ります。</summary>
    [Fact]
    public void ReadsSiteNameFromConfiguration()
    {
        using var provider = Build("検証サイト");

        var options = provider.GetRequiredService<IOptions<CommonOptions>>().Value;

        Assert.Equal("検証サイト", options.SiteName);
        // アプリ自身の名前はサイト名と別に保持します。
        Assert.Equal("検証ツール", options.ApplicationName);
    }

    /// <summary>サイト名が未設定なら起動時の構成エラーとし、アプリ名で代用しません。</summary>
    [Fact]
    public void RejectsMissingSiteName()
    {
        using var provider = Build(null);

        var options = provider.GetRequiredService<IOptions<CommonOptions>>();

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    /// <summary>メール文面の差込みは、アプリ自身の名前ではなくサイト名を使います。</summary>
    [Fact]
    public void MailTemplateUsesSiteNameNotApplicationName()
    {
        var renderer = new MailTemplateRenderer(Options.Create(new MailTemplateOptions()),
            Options.Create(new CommonOptions { SiteName = "検証サイト", ApplicationName = "検証ツール" }));

        var content = renderer.Render(MailTemplateKeys.InquiryReceipt, new Dictionary<string, string>
        {
            ["問い合わせID"] = "1",
            ["カテゴリ"] = "区分",
            ["対象"] = "対象",
            ["内容"] = "本文"
        });

        Assert.StartsWith("検証サイト", content.Subject);
        Assert.DoesNotContain("検証ツール", content.Subject);
        Assert.Contains("検証サイト", content.Body);
    }

    /// <summary>単独開発のツールとしてCommonを登録します。</summary>
    /// <param name="siteName">ホスト設定へ与えるサイト名です。NULLは未設定を表します。</param>
    /// <returns>登録済みのサービスです。</returns>
    private static ServiceProvider Build(string? siteName)
    {
        var values = new Dictionary<string, string?>
        {
            [ToolExecutionModeSettings.Key] = "Standalone",
            ["SalesSupport:Application:ToolId"] = "T001",
            ["SalesSupport:Application:Name"] = "検証ツール"
        };
        if (siteName is not null) values["SalesSupport:Site:Name"] = siteName;
        var services = new ServiceCollection().AddLogging();
        services.AddSalesSupportCommon(ApplicationKind.Tool, new StubEnvironment(),
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return services.BuildServiceProvider();
    }

    /// <summary>単独開発と同じ開発環境のホスト情報です。共通設定ファイルは読みません。</summary>
    private sealed class StubEnvironment : IHostEnvironment
    {
        /// <summary>単独開発が必要とする開発環境名です。</summary>
        public string EnvironmentName { get; set; } = "Development";
        /// <summary>テストホストの名前です。</summary>
        public string ApplicationName { get; set; } = "Tests";
        /// <summary>共通設定ファイルを読まないため使用しません。</summary>
        public string ContentRootPath { get; set; } = "";
        /// <summary>内容を読まないファイルプロバイダーです。</summary>
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
