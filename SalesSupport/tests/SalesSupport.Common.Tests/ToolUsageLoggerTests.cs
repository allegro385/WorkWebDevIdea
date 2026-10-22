using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Logging;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>ツールの利用ログを、設定のToolIdと固定の操作コードでCommonが組み立てることを確認します。</summary>
public sealed class ToolUsageLoggerTests
{
    /// <summary>実行の記録は、成功・失敗に応じたResultCodeの<c>WEB_EXECUTE</c>を1件にします。</summary>
    /// <param name="succeeded">業務処理が成功した場合はtrueです。</param>
    /// <param name="expectedResultCode">記録されるResultCodeです。</param>
    [Theory]
    [InlineData(true, "SUCCESS")]
    [InlineData(false, "FAILURE")]
    public async Task WritesExecuteWithConfiguredToolId(bool succeeded, string expectedResultCode)
    {
        var usage = new RecordingUsageLogger();
        var logger = new ToolUsageLogger(usage, Options.Create(new CommonOptions { ToolId = "T001" }));

        await logger.WriteExecuteAsync(succeeded);

        Assert.Equal([new UsageEvent("T001", "WEB_EXECUTE", expectedResultCode)], usage.Events);
    }

    /// <summary>入口の記録は<c>WEB_OPEN</c>の成功を1件にします。</summary>
    [Fact]
    public async Task WritesOpenAsSuccess()
    {
        var usage = new RecordingUsageLogger();
        var logger = new ToolUsageLogger(usage, Options.Create(new CommonOptions { ToolId = "T001" }));

        await logger.WriteOpenAsync();

        Assert.Equal([new UsageEvent("T001", "WEB_OPEN", "SUCCESS")], usage.Events);
    }

    /// <summary>ToolIdが未設定でも記録先の判断はCommonLoggerに委ね、空文字で渡します。</summary>
    [Fact]
    public async Task WritesEmptyToolIdWhenUnset()
    {
        var usage = new RecordingUsageLogger();
        var logger = new ToolUsageLogger(usage, Options.Create(new CommonOptions()));

        await logger.WriteExecuteAsync(succeeded: true);

        Assert.Equal("", Assert.Single(usage.Events).ToolId);
    }
}
