using SalesSupport.Portal.Web.Authentication;
using SalesSupport.Portal.Web.Bootstrap;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>初期管理者コマンドが既存管理者と秘密入力を安全に扱うことを確認します。</summary>
public sealed class InitialAdminBootstrapCommandTests
{
    /// <summary>有効な管理者が存在する場合は入力を求めず作成しないことを確認します。</summary>
    [Fact]
    public async Task ExistingActiveAdminStopsBeforeInput()
    {
        var provisioner = new StubProvisioner { HasActiveAdmin = true };
        var console = new StubConsole([], []);
        var command = CreateCommand(provisioner, console);

        var exitCode = await command.RunAsync();

        Assert.Equal(2, exitCode);
        Assert.Equal(0, provisioner.CreateCount);
        Assert.Equal(0, console.ReadLineCount);
        Assert.Equal(0, console.ReadSecretCount);
    }

    /// <summary>DB確認の例外を詳細表示せず終了コードへ変換することを確認します。</summary>
    [Fact]
    public async Task DatabaseCheckFailureReturnsFailure()
    {
        var provisioner = new StubProvisioner { ThrowOnCheck = true };
        var console = new StubConsole([], []);
        var command = CreateCommand(provisioner, console);

        var exitCode = await command.RunAsync();

        Assert.Equal(1, exitCode);
        Assert.Equal(0, console.ReadLineCount);
        Assert.Equal(0, console.ReadSecretCount);
    }

    /// <summary>確認入力が一致しない場合は再入力し、3回とも不一致なら作成しないことを確認します。</summary>
    [Fact]
    public async Task PasswordMismatchDoesNotCreateAdmin()
    {
        var provisioner = new StubProvisioner();
        var console = new StubConsole(["admin@example.com", "管理者"],
            ["ValidPassword!123", "DifferentPassword!123", "ValidPassword!123", "DifferentPassword!123", "ValidPassword!123", "DifferentPassword!123"]);
        var command = CreateCommand(provisioner, console);

        var exitCode = await command.RunAsync();

        Assert.Equal(3, exitCode);
        Assert.Equal(0, provisioner.CreateCount);
        Assert.Equal(6, console.ReadSecretCount);
    }

    /// <summary>一度不一致でも再入力が一致すれば作成できることを確認します。</summary>
    [Fact]
    public async Task PasswordMismatchCanBeRetried()
    {
        const string password = "ValidPassword!123";
        var provisioner = new StubProvisioner { CreateOutcome = InitialAdminProvisionOutcome.Created };
        var console = new StubConsole(["admin@example.com", "管理者"], [password, "DifferentPassword!123", password, password]);
        var command = CreateCommand(provisioner, console);

        var exitCode = await command.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, provisioner.CreateCount);
        Assert.Equal(4, console.ReadSecretCount);
    }

    /// <summary>禁止パスワードをDBへ渡さず入力エラーにすることを確認します。</summary>
    [Fact]
    public async Task ForbiddenPasswordDoesNotCreateAdmin()
    {
        const string forbidden = "ForbiddenPass!123";
        var provisioner = new StubProvisioner();
        var console = new StubConsole(["admin@example.com", "管理者"], [forbidden, forbidden]);
        var command = new InitialAdminBootstrapCommand(provisioner, PasswordPolicy.FromEntries([forbidden]), console);

        var exitCode = await command.RunAsync();

        Assert.Equal(3, exitCode);
        Assert.Equal(0, provisioner.CreateCount);
    }

    /// <summary>有効な入力では正規化したメール・表示名を一度だけ作成処理へ渡すことを確認します。</summary>
    [Fact]
    public async Task ValidInputCreatesAdminOnce()
    {
        const string password = "ValidPassword!123";
        var provisioner = new StubProvisioner { CreateOutcome = InitialAdminProvisionOutcome.Created };
        var console = new StubConsole([" admin@example.com ", " 管理者 "], [password, password]);
        var command = CreateCommand(provisioner, console);

        var exitCode = await command.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, provisioner.CreateCount);
        Assert.NotNull(provisioner.LastInput);
        Assert.Equal("admin@example.com", provisioner.LastInput.Email);
        Assert.Equal("管理者", provisioner.LastInput.DisplayName);
        Assert.Equal(password, provisioner.LastInput.Password);
    }

    /// <summary>禁止項目のない通常のパスワードポリシーでコマンドを生成します。</summary>
    private static InitialAdminBootstrapCommand CreateCommand(StubProvisioner provisioner, StubConsole console) =>
        new(provisioner, PasswordPolicy.FromEntries([]), console);

    /// <summary>作成処理の呼出しと入力を記録するテスト用実装です。</summary>
    private sealed class StubProvisioner : IInitialAdminProvisioner
    {
        public bool HasActiveAdmin { get; init; }
        public bool ThrowOnCheck { get; init; }
        public InitialAdminProvisionOutcome CreateOutcome { get; init; } = InitialAdminProvisionOutcome.Failed;
        public int CreateCount { get; private set; }
        public InitialAdminInput? LastInput { get; private set; }

        /// <summary>テストで指定した有効管理者の存在状態を返します。</summary>
        public Task<bool> HasActiveAdminAsync(CancellationToken ct = default) => ThrowOnCheck
            ? Task.FromException<bool>(new InvalidOperationException("接続先詳細を表示しないテスト例外"))
            : Task.FromResult(HasActiveAdmin);

        /// <summary>呼出し回数と入力を記録して指定結果を返します。</summary>
        public Task<InitialAdminProvisionOutcome> CreateAsync(InitialAdminInput input, CancellationToken ct = default)
        {
            CreateCount++;
            LastInput = input;
            return Task.FromResult(CreateOutcome);
        }
    }

    /// <summary>行入力・秘密入力を別々のキューから返すテスト用コンソールです。</summary>
    private sealed class StubConsole(IEnumerable<string> lines, IEnumerable<string> secrets) : IInitialAdminConsole
    {
        private readonly Queue<string> lines = new(lines);
        private readonly Queue<string> secrets = new(secrets);
        public int ReadLineCount { get; private set; }
        public int ReadSecretCount { get; private set; }

        /// <summary>テストでは表示内容を保存せず破棄します。</summary>
        public void WriteLine(string message) { }

        /// <summary>通常入力キューから次の値を返します。</summary>
        public string? ReadLine()
        {
            ReadLineCount++;
            return lines.Dequeue();
        }

        /// <summary>秘密入力キューから次の値を返します。</summary>
        public string ReadSecret()
        {
            ReadSecretCount++;
            return secrets.Dequeue();
        }
    }
}
