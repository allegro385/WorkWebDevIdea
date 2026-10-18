using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Data;
using SalesSupport.Common.DependencyInjection;
using SalesSupport.Common.Entities.Authentication;
using SalesSupport.Common.ErrorHandling;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Logging;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>デバッグ実行用の認証・ツールマスタ省略設定の環境制約と、組合せごとの利用制御・DB参照を確認します。</summary>
[Collection(CommonConfigPathCollection.Name)]
public sealed class ToolDevelopmentSwitchTests : IDisposable
{
    private readonly string folderName = "ss-switch-" + Guid.NewGuid().ToString("N");
    private readonly string folder;

    /// <summary>DI登録の検証に使う共通設定ファイルと鍵フォルダーを、実行フォルダー配下へ作成します。</summary>
    public ToolDevelopmentSwitchTests()
    {
        folder = Path.Combine(AppContext.BaseDirectory, folderName);
        Directory.CreateDirectory(Path.Combine(folder, "keys"));
    }

    /// <summary>検証用フォルダーを削除します。</summary>
    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* 検証環境の残存は無視します。 */ }
    }

    /// <summary>どちらの省略も、Commonとホストの両方が開発環境の場合だけ受け付けます。</summary>
    [Theory]
    [InlineData(ToolDevelopmentSettings.RequireAuthenticationKey, "DEVELOPMENT", "Development", false)]
    [InlineData(ToolDevelopmentSettings.RequireAuthenticationKey, "PRODUCTION", "Development", true)]
    [InlineData(ToolDevelopmentSettings.RequireAuthenticationKey, "DEVELOPMENT", "Production", true)]
    [InlineData(ToolDevelopmentSettings.RequireAuthenticationKey, "", "Development", true)]
    [InlineData(ToolDevelopmentSettings.UseToolMasterKey, "DEVELOPMENT", "Development", false)]
    [InlineData(ToolDevelopmentSettings.UseToolMasterKey, "PRODUCTION", "Development", true)]
    [InlineData(ToolDevelopmentSettings.UseToolMasterKey, "DEVELOPMENT", "Staging", true)]
    public void BypassRequiresBothDevelopmentEnvironments(string key, string environmentCode, string hostEnvironment, bool rejected)
    {
        var configuration = Configuration(environmentCode, new() { [key] = "false" });
        var host = new StubEnvironment { EnvironmentName = hostEnvironment };
        if (rejected) { Assert.Throws<ConfigurationException>(() => ToolDevelopmentSettings.Read(configuration, ApplicationKind.Tool, host)); return; }
        var switches = ToolDevelopmentSettings.Read(configuration, ApplicationKind.Tool, host);
        Assert.Equal(key != ToolDevelopmentSettings.RequireAuthenticationKey, switches.RequireAuthentication);
        Assert.Equal(key != ToolDevelopmentSettings.UseToolMasterKey, switches.UseToolMaster);
    }

    /// <summary>未設定は通常の確認とし、不正値やホスト不明での省略指定は拒否します。</summary>
    [Fact]
    public void MissingSettingsKeepNormalChecksAndInvalidValuesAreRejected()
    {
        Assert.Equal(new ToolDevelopmentSwitches(true, true), ToolDevelopmentSettings.Read(Configuration("PRODUCTION", new()), ApplicationKind.Tool, null));
        Assert.Equal(new ToolDevelopmentSwitches(true, true), ToolDevelopmentSettings.Read(
            Configuration("PRODUCTION", new() { [ToolDevelopmentSettings.RequireAuthenticationKey] = "true", [ToolDevelopmentSettings.UseToolMasterKey] = "true" }), ApplicationKind.Tool, null));
        Assert.Throws<ConfigurationException>(() => ToolDevelopmentSettings.Read(
            Configuration("DEVELOPMENT", new() { [ToolDevelopmentSettings.UseToolMasterKey] = "invalid" }), ApplicationKind.Tool, new StubEnvironment()));
        Assert.Throws<ConfigurationException>(() => ToolDevelopmentSettings.Read(
            Configuration("DEVELOPMENT", new() { [ToolDevelopmentSettings.RequireAuthenticationKey] = "false" }), ApplicationKind.Tool, null));
    }

    /// <summary>共通設定を共有してもPortalは認証・ツールマスタの確認を省略しません。</summary>
    [Fact]
    public void PortalIgnoresSwitches() => Assert.Equal(new ToolDevelopmentSwitches(true, true), ToolDevelopmentSettings.Read(
        Configuration("DEVELOPMENT", new() { [ToolDevelopmentSettings.RequireAuthenticationKey] = "false", [ToolDevelopmentSettings.UseToolMasterKey] = "false" }),
        ApplicationKind.Portal, new StubEnvironment()));

    /// <summary>ツールマスタ省略では設定したToolIdだけをDBなしで許可し、利用者のいない要求は拒否します。</summary>
    [Fact]
    public async Task ToolMasterBypassAllowsConfiguredToolWithoutDatabase()
    {
        var options = Options.Create(Tool(requireAuthentication: true, useToolMaster: false));
        var current = new CurrentUserAccessor();
        var evaluator = new AccessEvaluator(current, new RejectSettings(), new RejectFactory(), options);
        Assert.Equal(401, (await evaluator.EvaluateAsync(new(AccessPurpose.ToolUse, "T001"))).StatusCode);
        current.SetVerified(new CurrentUser(Guid.NewGuid(), "検証用", "A"));
        Assert.True((await evaluator.EvaluateAsync(new(AccessPurpose.ToolUse, "T001"))).Allowed);
        Assert.Equal(400, (await evaluator.EvaluateAsync(new(AccessPurpose.ToolUse, "OTHER"))).StatusCode);
    }

    /// <summary>本番区分では省略フラグを持っていても、未認証のツール利用を許可しません。</summary>
    [Fact]
    public async Task ProductionDoesNotBypassChecks()
    {
        var options = Options.Create(new CommonOptions { Kind = ApplicationKind.Tool, EnvironmentCode = "PRODUCTION", ToolId = "T001", RequireAuthentication = false, UseToolMaster = false });
        Assert.False(options.Value.IsAuthenticationBypassed || options.Value.IsToolMasterBypassed || !options.Value.UsesDatabase);
        var evaluator = new AccessEvaluator(new CurrentUserAccessor(), new RejectSettings(), new RejectFactory(), options);
        Assert.Equal(401, (await evaluator.EvaluateAsync(new(AccessPurpose.ToolUse, "T001"))).StatusCode);
    }

    /// <summary>通常運用ではDBのサイト・ツール状態とロール割当てを読んで判定します。</summary>
    [Theory]
    [InlineData("PUBLIC", "PUBLIC", "A", true, 200)]
    [InlineData("PUBLIC", "PRIVATE", "A", true, 403)]
    [InlineData("PUBLIC", "PRIVATE", "ADMIN", false, 200)]
    [InlineData("PUBLIC", "HIDDEN", "ADMIN", false, 404)]
    [InlineData("PUBLIC", "PUBLIC", "A", false, 403)]
    [InlineData("PRIVATE", "PUBLIC", "A", true, 403)]
    [InlineData("PUBLIC", null, "ADMIN", false, 404)]
    public async Task DatabaseStateControlsNormalAccess(string siteStatus, string? toolStatus, string roleCode, bool assigned, int expectedStatus)
    {
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(Guid.NewGuid(), "検証用", roleCode));
        var decision = await EvaluateWithDatabaseAsync(current, new CommonOptions { Kind = ApplicationKind.Tool, EnvironmentCode = "PRODUCTION", ToolId = "T001" },
            siteStatus, toolStatus, roleCode, assigned);
        Assert.Equal(expectedStatus, decision.StatusCode);
        Assert.Equal(expectedStatus == 200, decision.Allowed);
    }

    /// <summary>認証だけを省略した仮管理者も、ツールマスタの登録・状態・種別で判定します。ロール割当ては不要です。</summary>
    [Theory]
    [InlineData("PUBLIC", 200)]
    [InlineData("PRIVATE", 200)]
    [InlineData("HIDDEN", 404)]
    [InlineData(null, 404)]
    public async Task DevelopmentUserUsesToolMasterWhenEnabled(string? toolStatus, int expectedStatus)
    {
        var current = new CurrentUserAccessor();
        current.SetVerified(DevelopmentUser.Create());
        var decision = await EvaluateWithDatabaseAsync(current, Tool(requireAuthentication: false, useToolMaster: true), "PRIVATE", toolStatus, DevelopmentUser.RoleCode, assigned: false);
        Assert.Equal(expectedStatus, decision.StatusCode);
    }

    /// <summary>開発用ハンドラーは固定の仮管理者を認証済みとし、同じ要求の現行ユーザーへ設定します。</summary>
    [Fact]
    public async Task DevelopmentHandlerAuthenticatesFixedAdministrator()
    {
        var current = new CurrentUserAccessor();
        var handler = new DevelopmentUserAuthenticationHandler(new StaticOptionsMonitor(), NullLoggerFactory.Instance, UrlEncoder.Default, current);
        await handler.InitializeAsync(new AuthenticationScheme(DevelopmentUserAuthenticationHandler.SchemeName, null, typeof(DevelopmentUserAuthenticationHandler)), new DefaultHttpContext());
        var result = await handler.AuthenticateAsync();
        Assert.True(result.Succeeded);
        Assert.Equal(DevelopmentUser.UserId.ToString(), result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(DevelopmentUser.LoginId, result.Principal.Identity!.Name);
        Assert.Equal(new CurrentUser(DevelopmentUser.UserId, DevelopmentUser.LoginId, "ADMIN"), current.User);
        Assert.NotEqual(Guid.Empty, DevelopmentUser.UserId);
    }

    /// <summary>両方を省略した場合だけDBを使わず、ツールマスタだけの省略では業務日付をDBから読みます。</summary>
    [Fact]
    public async Task DatabaseIsSkippedOnlyWhenBothChecksAreBypassed()
    {
        var databaseFree = Options.Create(Tool(requireAuthentication: false, useToolMaster: false));
        Assert.False(databaseFree.Value.UsesDatabase);
        Assert.Null(await new DatabaseSettingsReader(new RejectFactory(), databaseFree).GetBusinessDateAsync());
        var masterOnly = Options.Create(Tool(requireAuthentication: true, useToolMaster: false));
        Assert.True(masterOnly.Value.UsesDatabase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DatabaseSettingsReader(new RejectFactory(), masterOnly).GetBusinessDateAsync());
    }

    /// <summary>ツールマスタ省略ではCommonがツール単位のアップロードを拒否し、固定のポリシーで代用しません。</summary>
    [Theory]
    [InlineData(UploadPurpose.ToolInput)]
    [InlineData(UploadPurpose.Reference)]
    [InlineData(UploadPurpose.App)]
    public async Task ToolUploadIsRejectedWhenToolMasterIsBypassed(UploadPurpose purpose)
    {
        var provider = new UploadPolicyProvider(new RejectFactory(), Options.Create(Tool(requireAuthentication: true, useToolMaster: false)));
        var exception = await Assert.ThrowsAsync<UploadRejectedException>(() => provider.GetAsync(purpose, "T001"));
        Assert.Equal("UPLOAD_UNAVAILABLE", exception.Error.Code);
    }

    /// <summary>ツールマスタ省略では仮管理者の利用ログもDBへ保存しません。</summary>
    [Fact]
    public async Task UsageLogIsSkippedWhenToolMasterIsBypassed()
    {
        var current = new CurrentUserAccessor();
        current.SetVerified(DevelopmentUser.Create());
        var logger = new CommonLogger(new RejectLogFactory(), current, new SalesSupport.Common.DateTime.ApplicationClock(TimeProvider.System), new RequestCorrelation(),
            Options.Create(Tool(requireAuthentication: false, useToolMaster: false)), Options.Create(new LoggingOptions { TimeoutSeconds = 3 }), new HttpContextAccessor());
        Assert.Equal(LogWriteResult.Skipped, await logger.WriteAsync(new UsageEvent("T001", "WEB_OPEN", "SUCCESS")));
    }

    /// <summary>省略設定がある場合だけ、障害の詳細をホストのログ（コンソール）へ出力します。</summary>
    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    public async Task ErrorIsWrittenToConsoleOnlyWithDevelopmentBypass(bool requireAuthentication, bool useToolMaster, int expectedCount)
    {
        var console = new CapturingLogger();
        var handler = new CommonErrorHandler(new RejectFactory(), new SkippingErrorLogger(), Options.Create(Tool(requireAuthentication, useToolMaster)), console);
        var failure = new InvalidOperationException("検証用の障害");
        var result = await handler.HandleAsync(new(Exception: failure));
        Assert.Equal(expectedCount, console.Entries.Count);
        if (expectedCount == 1) Assert.Same(failure, console.Entries[0]);
        Assert.NotEqual(Guid.Empty, result.ErrorId);
    }

    /// <summary>DI登録が省略設定に応じて既定の認証方式・接続文字列の要否・明示ポリシーの認証方式を切り替えます。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task RegistrationFollowsSwitches(bool requireAuthentication, bool useToolMaster)
    {
        var connection = requireAuthentication || useToolMaster ? "Data Source=verify;" : "";
        var settings = new
        {
            ConnectionStrings = new { SalesSupport = connection },
            Portal = new { EnvironmentCode = "DEVELOPMENT" },
            SalesSupport = new
            {
                Tool = new { RequireAuthentication = requireAuthentication, UseToolMaster = useToolMaster },
                Application = new { Name = "検証", ToolId = "T001" },
                Portal = new { BaseUrl = "https://example.invalid/" },
                DataProtection = new { KeyDirectory = "keys" }
            }
        };
        File.WriteAllText(Path.Combine(folder, "common.json"), System.Text.Json.JsonSerializer.Serialize(settings));
        var original = Environment.GetEnvironmentVariable(CommonConfiguration.PathVariable);
        Environment.SetEnvironmentVariable(CommonConfiguration.PathVariable, folderName + "/common.json");
        try
        {
            var services = new ServiceCollection().AddLogging();
            services.AddSalesSupportCommon(ApplicationKind.Tool, new StubEnvironment());
            await using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<CommonOptions>>().Value;
            Assert.Equal(!requireAuthentication, options.IsAuthenticationBypassed);
            Assert.Equal(!useToolMaster, options.IsToolMasterBypassed);
            var expectedScheme = requireAuthentication ? SharedCookieContract.Scheme : DevelopmentUserAuthenticationHandler.SchemeName;
            Assert.Equal(expectedScheme, (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync())!.Name);
            var authorization = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
            Assert.Equal([expectedScheme], authorization.FallbackPolicy!.AuthenticationSchemes);
            // 明示ポリシーは共有Cookieの実ユーザーだけを対象とし、仮管理者では満たしません。
            Assert.Equal([SharedCookieContract.Scheme], authorization.GetPolicy("SalesSupportTool")!.AuthenticationSchemes);
            Assert.Equal([SharedCookieContract.Scheme], authorization.GetPolicy("SalesSupportAdmin")!.AuthenticationSchemes);
        }
        finally { Environment.SetEnvironmentVariable(CommonConfiguration.PathVariable, original); }
    }

    /// <summary>DBのサイト・ツール状態とロールを投入し、指定した利用者・設定でツール利用を判定します。</summary>
    /// <param name="current">判定に使う現行ユーザーです。</param>
    /// <param name="options">判定に使う共通設定です。</param>
    /// <param name="siteStatus">サイト公開状態です。</param>
    /// <param name="toolStatus">ツールの状態です。NULLはツール行なしを表します。</param>
    /// <param name="roleCode">投入するロールです。</param>
    /// <param name="assigned">ツールへロールを割り当てるかです。</param>
    /// <returns>T001のツール利用判定です。</returns>
    private static async Task<AccessDecision> EvaluateWithDatabaseAsync(CurrentUserAccessor current, CommonOptions options, string siteStatus, string? toolStatus, string roleCode, bool assigned)
    {
        var root = new InMemoryDatabaseRoot();
        var name = Guid.NewGuid().ToString("N");
        await using (var seed = new SeedContext(new DbContextOptionsBuilder<SeedContext>().UseInMemoryDatabase(name, root).Options))
        {
            seed.Add(new RoleAccessRecord { RoleCode = roleCode });
            if (toolStatus is not null) seed.Add(new ToolAccessRecord { ToolId = "T001", ToolType = "WEB", Status = toolStatus });
            if (assigned) seed.Add(new ToolRoleAccessRecord { ToolId = "T001", RoleCode = roleCode });
            seed.Add(new SalesSupport.Common.Entities.Configuration.SystemSetting { SettingCategory = "SITE", SettingKey = "PUBLICATION_STATUS", SettingValue = siteStatus });
            await seed.SaveChangesAsync();
        }
        var factory = new MemoryFactory(new DbContextOptionsBuilder<CommonDbContext>().UseInMemoryDatabase(name, root).Options);
        var settings = new DatabaseSettingsReader(factory, Options.Create(new CommonOptions { EnvironmentCode = "PRODUCTION" }));
        return await new AccessEvaluator(current, settings, factory, Options.Create(options)).EvaluateAsync(new(AccessPurpose.ToolUse, "T001"));
    }

    /// <summary>開発環境のツール設定を、指定した省略設定で作成します。</summary>
    /// <param name="requireAuthentication">共有Cookie認証を行うかです。</param>
    /// <param name="useToolMaster">ツールマスタを参照するかです。</param>
    /// <returns>ToolIdがT001の共通設定です。</returns>
    private static CommonOptions Tool(bool requireAuthentication, bool useToolMaster) => new()
    {
        Kind = ApplicationKind.Tool, EnvironmentCode = "DEVELOPMENT", ToolId = "T001", RequireAuthentication = requireAuthentication, UseToolMaster = useToolMaster
    };

    /// <summary>秘密値を含まない設定だけをメモリ上に組み立てます。</summary>
    /// <param name="environmentCode">Commonの環境区分です。</param>
    /// <param name="values">追加する設定です。</param>
    /// <returns>検証用の設定です。</returns>
    private static IConfiguration Configuration(string environmentCode, Dictionary<string, string?> values)
    {
        values["Portal:EnvironmentCode"] = environmentCode;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>起動環境名だけを指定するホスト情報です。</summary>
    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>DBの代替や隠れた参照が発生したら即座に失敗させます。</summary>
    private sealed class RejectFactory : IDbContextFactory<CommonDbContext>
    {
        /// <summary>DBを参照してはいけない検証でContextの作成を検出します。</summary>
        public CommonDbContext CreateDbContext() => throw new InvalidOperationException("DBを参照しました。");
    }

    /// <summary>ログ保存のためのDB参照を検出します。</summary>
    private sealed class RejectLogFactory : IDbContextFactory<LogDbContext>
    {
        /// <summary>ログを保存してはいけない検証でContextの作成を検出します。</summary>
        public LogDbContext CreateDbContext() => throw new InvalidOperationException("ログDBを参照しました。");
    }

    /// <summary>サイトのDB設定を取得してはいけない検証で参照を検出します。</summary>
    private sealed class RejectSettings : ISystemSettingsReader
    {
        /// <summary>サイト公開状態のDB取得を検出します。</summary>
        public Task<string> GetPublicationStatusAsync(CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>非公開案内のDB取得を検出します。</summary>
        public Task<string> GetPrivateMessageAsync(CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>業務日付のDB取得を検出します。</summary>
        public Task<DateOnly?> GetBusinessDateAsync(CancellationToken ct = default) => throw new InvalidOperationException();
    }

    /// <summary>DBの障害ログを保存せず、記録されなかったことを返します。</summary>
    private sealed class SkippingErrorLogger : ISystemErrorLogger
    {
        /// <summary>省略時と同じ結果を返します。</summary>
        public Task<LogWriteResult> WriteAsync(SystemErrorEvent entry, CancellationToken ct = default) => Task.FromResult(LogWriteResult.Skipped);
    }

    /// <summary>ホストのログへ渡された例外を記録します。</summary>
    private sealed class CapturingLogger : ILogger<CommonErrorHandler>
    {
        /// <summary>出力された例外です。</summary>
        public List<Exception?> Entries { get; } = [];
        /// <summary>スコープは使用しません。</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>すべての水準を有効とします。</summary>
        public bool IsEnabled(LogLevel logLevel) => true;
        /// <summary>出力された例外だけを保持します。</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Entries.Add(exception);
    }

    /// <summary>認証ハンドラーへ既定の設定を渡します。</summary>
    private sealed class StaticOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        /// <summary>既定の設定です。</summary>
        public AuthenticationSchemeOptions CurrentValue { get; } = new();
        /// <summary>名前にかかわらず既定の設定を返します。</summary>
        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;
        /// <summary>変更通知は使用しません。</summary>
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    /// <summary>SQLへ接続しない要求ごとの読取りContextを供給します。</summary>
    private sealed class MemoryFactory(DbContextOptions<CommonDbContext> options) : IDbContextFactory<CommonDbContext>
    {
        /// <summary>専用のメモリ上DBを読むContextを作成します。</summary>
        public CommonDbContext CreateDbContext() => new(options);
    }

    /// <summary>既存の読取りモデルと同じメモリ上DBへ使い捨てデータを投入します。</summary>
    private sealed class SeedContext(DbContextOptions<SeedContext> options) : DbContext(options)
    {
        /// <summary>共通の列・キー定義を適用します。</summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder) => CommonMappings.ConfigureReadModels(modelBuilder);
    }
}

/// <summary>共通設定ファイルの場所を与えるプロセス全体の環境変数を扱う検証を、並列に実行しないためのグループです。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CommonConfigPathCollection
{
    /// <summary>グループ名です。</summary>
    public const string Name = "CommonConfigPath";
}
