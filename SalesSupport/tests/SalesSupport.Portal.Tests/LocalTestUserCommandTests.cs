using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Portal.Web.Authentication;
using SalesSupport.Portal.Web.Bootstrap;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>開発専用コマンドの環境制限と複数ユーザー追加を確認します。</summary>
public sealed class LocalTestUserCommandTests
{
    /// <summary>ホストが本番環境なら入力を求めず作成を拒否します。</summary>
    [Fact]
    public async Task ProductionHostRejectsBeforeInput()
    {
        var provisioner = new StubProvisioner();
        var console = new StubConsole([], []);
        var command = CreateCommand("Production", "DEVELOPMENT", provisioner, console);

        Assert.Equal(2, await command.RunAsync());
        Assert.Empty(provisioner.Inputs);
        Assert.Equal(0, console.ReadLineCount);
    }

    /// <summary>Portal側が本番設定なら入力を求めず作成を拒否します。</summary>
    [Fact]
    public async Task ProductionPortalRejectsBeforeInput()
    {
        var provisioner = new StubProvisioner();
        var console = new StubConsole([], []);
        var command = CreateCommand("Development", "PRODUCTION", provisioner, console);

        Assert.Equal(2, await command.RunAsync());
        Assert.Empty(provisioner.Inputs);
        Assert.Equal(0, console.ReadLineCount);
    }

    /// <summary>既存管理者の有無に関係なく、別々の一般ユーザーを順に追加できます。</summary>
    [Fact]
    public async Task DevelopmentCanAddMoreThanOneUser()
    {
        const string password = "ValidPassword!123";
        var provisioner = new StubProvisioner();
        var console = new StubConsole([" user1@example.invalid ", " 一人目 ", "user2@example.invalid", "二人目"],
            [password, password, password, password]);
        var command = CreateCommand("Development", "DEVELOPMENT", provisioner, console);

        Assert.Equal(0, await command.RunAsync());
        Assert.Equal(0, await command.RunAsync());
        Assert.Equal(["user1@example.invalid", "user2@example.invalid"], provisioner.Inputs.Select(x => x.Email));
        Assert.All(provisioner.Inputs, input => Assert.Equal(password, input.Password));
        Assert.DoesNotContain(console.Messages, message => message.Contains(password, StringComparison.Ordinal));
    }

    /// <summary>禁止パスワードは保存処理へ渡しません。</summary>
    [Fact]
    public async Task ForbiddenPasswordIsRejected()
    {
        const string password = "ForbiddenPass!123";
        var provisioner = new StubProvisioner();
        var console = new StubConsole(["user@example.invalid", "利用者"], [password, password]);
        var environment = new StubEnvironment { EnvironmentName = "Development" };
        var options = Options.Create(new CommonOptions { EnvironmentCode = "DEVELOPMENT" });
        var command = new LocalTestUserCommand(environment, options, provisioner, PasswordPolicy.FromEntries([password]), console);

        Assert.Equal(3, await command.RunAsync());
        Assert.Empty(provisioner.Inputs);
    }

    /// <summary>テスト対象を指定した二つの環境設定で生成します。</summary>
    private static LocalTestUserCommand CreateCommand(string hostEnvironment, string portalEnvironment,
        StubProvisioner provisioner, StubConsole console)
    {
        var environment = new StubEnvironment { EnvironmentName = hostEnvironment };
        var options = Options.Create(new CommonOptions { EnvironmentCode = portalEnvironment });
        return new LocalTestUserCommand(environment, options, provisioner, PasswordPolicy.FromEntries([]), console);
    }

    /// <summary>保存要求を記録するテスト用の作成処理です。</summary>
    private sealed class StubProvisioner : ILocalTestUserProvisioner
    {
        public List<LocalTestUserInput> Inputs { get; } = [];

        /// <summary>作成対象を記録し成功を返します。</summary>
        public Task<LocalTestUserOutcome> CreateAsync(LocalTestUserInput input, CancellationToken ct = default)
        {
            Inputs.Add(input);
            return Task.FromResult(LocalTestUserOutcome.Created);
        }
    }

    /// <summary>通常入力と秘密入力を分けて渡すテスト用コンソールです。</summary>
    private sealed class StubConsole(IEnumerable<string> lines, IEnumerable<string> secrets) : IInitialAdminConsole
    {
        private readonly Queue<string> lines = new(lines);
        private readonly Queue<string> secrets = new(secrets);
        public int ReadLineCount { get; private set; }
        public List<string> Messages { get; } = [];

        /// <summary>表示内容を保持して秘密が含まれないことを検査します。</summary>
        public void WriteLine(string message) => Messages.Add(message);

        /// <summary>通常入力を一行取り出します。</summary>
        public string? ReadLine()
        {
            ReadLineCount++;
            return lines.Dequeue();
        }

        /// <summary>秘密入力を表示せず一行取り出します。</summary>
        public string ReadSecret() => secrets.Dequeue();
    }

    /// <summary>ASP.NET Coreの環境名だけを切り替えるテスト用ホスト環境です。</summary>
    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "SalesSupport.Portal.Web";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
