using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DependencyInjection;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>一時保存領域を使うツール向けの共通検証が、連携モードでだけ設定を要求することを確認します。</summary>
public sealed class ToolStorageRequirementTests
{
    /// <summary>連携モードでは一時保存領域の未設定を構成エラーとし、単独開発では検証しません。</summary>
    /// <param name="standalone">単独開発として起動する場合はtrueです。</param>
    /// <param name="temporaryRoot">設定された一時保存領域です。未設定はnullです。</param>
    /// <param name="expectedValid">設定を取得できる場合はtrueです。</param>
    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, @"C:\ss-temporary", true)]
    [InlineData(true, null, true)]
    public void TemporaryRootIsRequiredUnlessStandalone(bool standalone, string? temporaryRoot, bool expectedValid)
    {
        var services = new ServiceCollection();
        services.AddOptions<CommonOptions>().Configure(options =>
        {
            options.Kind = ApplicationKind.Tool;
            options.EnvironmentCode = "DEVELOPMENT";
            options.ExecutionMode = standalone ? ToolExecutionMode.Standalone : ToolExecutionMode.Portal;
        });
        services.AddOptions<StorageOptions>().Configure(options => options.TemporaryRoot = temporaryRoot);
        services.RequireTemporaryStorage();
        using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IOptions<StorageOptions>>();

        if (expectedValid) Assert.Equal(temporaryRoot, storage.Value.TemporaryRoot);
        else Assert.Throws<OptionsValidationException>(() => storage.Value);
    }
}
