using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>起動モードと認証モードから、本人を特定できる利用者がいるかの判定を確認します。</summary>
public sealed class CommonOptionsModeTests
{
    /// <summary>共有Cookieで本人を認証する通常構成では、本人向けデータを扱えます。</summary>
    [Fact]
    public void IdentifiesUserInEmailOtpMode()
    {
        var options = new CommonOptions
        {
            Kind = ApplicationKind.Tool, EnvironmentCode = "PRODUCTION",
            ExecutionMode = ToolExecutionMode.Portal, AuthenticationMode = SiteAuthenticationMode.EmailOtp
        };

        Assert.True(options.HasIdentifiedUser);
    }

    /// <summary>匿名モードの固定共通ユーザーはAspNetUsersに存在しないため、本人向けデータを扱えません。</summary>
    [Fact]
    public void DoesNotIdentifyUserInAnonymousMode()
    {
        var options = new CommonOptions
        {
            Kind = ApplicationKind.Tool, EnvironmentCode = "PRODUCTION",
            ExecutionMode = ToolExecutionMode.Portal, AuthenticationMode = SiteAuthenticationMode.Anonymous
        };

        Assert.True(options.IsAnonymous);
        Assert.False(options.HasIdentifiedUser);
    }

    /// <summary>単独開発には認証ユーザーとDBがないため、本人向けデータを扱えません。</summary>
    [Fact]
    public void DoesNotIdentifyUserInStandaloneMode()
    {
        var options = new CommonOptions
        {
            Kind = ApplicationKind.Tool, EnvironmentCode = "DEVELOPMENT",
            ExecutionMode = ToolExecutionMode.Standalone, AuthenticationMode = SiteAuthenticationMode.EmailOtp
        };

        Assert.True(options.IsStandalone);
        Assert.False(options.HasIdentifiedUser);
    }
}
