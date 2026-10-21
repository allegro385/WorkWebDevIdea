using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

/// <summary>ツールの起動モード（単独開発・ポータル連携）の受付条件と、単独開発での利用制御・DB非参照・DI登録を確認します。</summary>
[Collection(CommonConfigPathCollection.Name)]
public sealed class ToolExecutionModeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ss-mode-" + Guid.NewGuid().ToString("N"));
    private readonly string contentRoot;

    /// <summary>標準配置（<c>apps/T001</c>と<c>config</c>・<c>keys</c>）を模した検証用フォルダーを作成します。共通設定ファイルは作りません。</summary>
    public ToolExecutionModeTests()
    {
        contentRoot = Path.Combine(root, "apps", "T001");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "keys"));
    }

    /// <summary>検証用フォルダーを削除します。</summary>
    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { /* 検証環境の残存は無視します。 */ }
    }

    /// <summary>単独開発はToolかつホストDevelopmentだけで受け付け、未設定は連携モード、それ以外の指定と未定義値は構成エラーにします。</summary>
    [Theory]
    [InlineData(ApplicationKind.Tool, "Development", null, ToolExecutionMode.Portal)]
    [InlineData(ApplicationKind.Tool, "Production", null, ToolExecutionMode.Portal)]
    [InlineData(ApplicationKind.Tool, "Production", "Portal", ToolExecutionMode.Portal)]
    [InlineData(ApplicationKind.Portal, "Development", null, ToolExecutionMode.Portal)]
    [InlineData(ApplicationKind.Tool, "Development", "Standalone", ToolExecutionMode.Standalone)]
    [InlineData(ApplicationKind.Tool, "Production", "Standalone", null)]
    [InlineData(ApplicationKind.Tool, "Staging", "Standalone", null)]
    [InlineData(ApplicationKind.Portal, "Development", "Standalone", null)]
    [InlineData(ApplicationKind.Tool, "Development", "standalone", null)]
    [InlineData(ApplicationKind.Tool, "Development", "true", null)]
    public void ReadAcceptsStandaloneOnlyForToolInDevelopment(ApplicationKind kind, string hostEnvironment, string? value, ToolExecutionMode? expected)
    {
        var configuration = Host(new() { [ToolExecutionModeSettings.Key] = value });
        var host = new StubEnvironment { EnvironmentName = hostEnvironment };
        if (expected is null)
        {
            var exception = Assert.Throws<ConfigurationException>(() => ToolExecutionModeSettings.Read(configuration, kind, host));
            Assert.Contains(ToolExecutionModeSettings.Key, exception.Message);
            return;
        }
        Assert.Equal(expected, ToolExecutionModeSettings.Read(configuration, kind, host));
    }

    /// <summary>単独開発の判定は、本番区分やPortalの設定値では成立しません。</summary>
    [Fact]
    public void StandaloneRequiresToolAndDevelopmentCode()
    {
        Assert.True(Standalone().IsStandalone);
        Assert.False(new CommonOptions { Kind = ApplicationKind.Tool, EnvironmentCode = "PRODUCTION", ExecutionMode = ToolExecutionMode.Standalone }.IsStandalone);
        Assert.False(new CommonOptions { Kind = ApplicationKind.Portal, EnvironmentCode = "DEVELOPMENT", ExecutionMode = ToolExecutionMode.Standalone }.IsStandalone);
        Assert.False(Portal().IsStandalone);
    }

    /// <summary>単独開発では利用者もDBもなしに設定したToolIdの利用だけを許可し、他ツール・サイト入場へ広げません。</summary>
    [Fact]
    public async Task StandaloneAllowsOnlyConfiguredToolWithoutUserOrDatabase()
    {
        var evaluator = new AccessEvaluator(new CurrentUserAccessor(), new RejectSettings(), new RejectFactory(), Options.Create(Standalone()));
        Assert.True((await evaluator.EvaluateAsync(new(AccessPurpose.ToolUse, "T001"))).Allowed);
        Assert.Equal(400, (await evaluator.EvaluateAsync(new(AccessPurpose.ToolUse, "OTHER"))).StatusCode);
        Assert.Equal(401, (await evaluator.EvaluateAsync(new(AccessPurpose.Site))).StatusCode);
    }

    /// <summary>連携モードでは利用者のいない要求を許可しません。</summary>
    [Fact]
    public async Task PortalModeRequiresUser()
    {
        var evaluator = new AccessEvaluator(new CurrentUserAccessor(), new RejectSettings(), new RejectFactory(), Options.Create(Portal()));
        Assert.Equal(401, (await evaluator.EvaluateAsync(new(AccessPurpose.ToolUse, "T001"))).StatusCode);
    }

    /// <summary>単独開発の認証なし利用は自端末からの要求だけに限り、他の接続元はツールが存在しない扱いで拒否します。</summary>
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.0.10", false)]
    [InlineData(null, false)]
    public async Task StandaloneAcceptsOnlyLoopbackRequests(string? address, bool allowed)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = address is null ? null : IPAddress.Parse(address);
        var http = new HttpContextAccessor { HttpContext = context };
        var current = new CurrentUserAccessor();
        var options = Options.Create(Standalone());
        var handler = new CommonAuthorizationHandler(new AccessEvaluator(current, new RejectSettings(), new RejectFactory(), options), current, options, http);
        var authorization = new AuthorizationHandlerContext([new CommonAccessRequirement(Tool: true)], new ClaimsPrincipal(), null);
        await handler.HandleAsync(authorization);
        Assert.Equal(allowed, authorization.HasSucceeded);
        if (!allowed) Assert.Equal(new AccessDecision(false, 404, "TOOL_UNAVAILABLE"), context.Items["SalesSupport.AccessDecision"]);
    }

    /// <summary>単独開発で共有Cookieを要求する画面（明示ポリシー）は、Portalがないためログインへ誘導せず401を返します。</summary>
    [Fact]
    public async Task StandaloneCookieChallengeReturnsUnauthorizedWithoutPortalRedirect()
    {
        var events = new SharedCookieEvents(new RejectFactory(), new CurrentUserAccessor(), new SalesSupport.Common.DateTime.ApplicationClock(TimeProvider.System),
            Options.Create(Standalone()), Options.Create(new IdentityOptions()));
        var context = new DefaultHttpContext();
        context.Request.Path = "/records";
        var scheme = new AuthenticationScheme(SharedCookieContract.Scheme, null, typeof(CookieAuthenticationHandler));
        await events.RedirectToLogin(new RedirectContext<CookieAuthenticationOptions>(context, scheme, new CookieAuthenticationOptions(), new AuthenticationProperties(), "/account/login"));
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
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

    /// <summary>単独開発では業務日付をDBから読まず、現在のJST日付を使わせます。</summary>
    [Fact]
    public async Task StandaloneDoesNotReadBusinessDate() =>
        Assert.Null(await new DatabaseSettingsReader(new RejectFactory(), Options.Create(Standalone())).GetBusinessDateAsync());

    /// <summary>単独開発ではCommonがツール単位のアップロードを拒否し、固定のポリシーで代用しません。</summary>
    [Theory]
    [InlineData(UploadPurpose.ToolInput)]
    [InlineData(UploadPurpose.Reference)]
    [InlineData(UploadPurpose.App)]
    public async Task ToolUploadIsRejectedInStandalone(UploadPurpose purpose)
    {
        var provider = new UploadPolicyProvider(new RejectFactory(), Options.Create(Standalone()));
        var exception = await Assert.ThrowsAsync<UploadRejectedException>(() => provider.GetAsync(purpose, "T001"));
        Assert.Equal("UPLOAD_UNAVAILABLE", exception.Error.Code);
    }

    /// <summary>単独開発では利用ログをDBへ保存しません。</summary>
    [Fact]
    public async Task UsageLogIsSkippedInStandalone()
    {
        var logger = new CommonLogger(new RejectLogFactory(), new CurrentUserAccessor(), new SalesSupport.Common.DateTime.ApplicationClock(TimeProvider.System), new RequestCorrelation(),
            Options.Create(Standalone()), Options.Create(new LoggingOptions { TimeoutSeconds = 3 }), new HttpContextAccessor());
        Assert.Equal(LogWriteResult.Skipped, await logger.WriteAsync(new UsageEvent("T001", "WEB_OPEN", "SUCCESS")));
    }

    /// <summary>単独開発の場合だけ、障害の詳細をホストのログ（コンソール）へ出力します。</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task ErrorIsWrittenToConsoleOnlyInStandalone(bool standalone, int expectedCount)
    {
        var console = new CapturingLogger();
        var handler = new CommonErrorHandler(new RejectFactory(), new SkippingErrorLogger(), Options.Create(standalone ? Standalone() : Portal()), console);
        var failure = new InvalidOperationException("検証用の障害");
        var result = await handler.HandleAsync(new(Exception: failure));
        Assert.Equal(expectedCount, console.Entries.Count);
        if (expectedCount == 1) Assert.Same(failure, console.Entries[0]);
        Assert.NotEqual(Guid.Empty, result.ErrorId);
    }

    /// <summary>単独開発は共通設定ファイル・Portal URL・接続文字列・鍵・保存領域なしで登録でき、起動時の設定検証も通ります。</summary>
    [Fact]
    public async Task StandaloneRegistersWithoutCommonConfiguration()
    {
        await using var provider = RegisterStandalone();
        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptions<CommonOptions>>().Value;
        Assert.True(options.IsStandalone);
        Assert.Equal("DEVELOPMENT", options.EnvironmentCode);
        Assert.Equal("T001", options.ToolId);
        Assert.Equal("検証ツール", options.ApplicationName);
        Assert.Null(provider.GetRequiredService<IOptions<StorageOptions>>().Value.TemporaryRoot);
        Assert.Throws<ConfigurationException>(() => provider.GetRequiredService<IConnectionStringProvider>().SalesSupportDatabase);
        Assert.Equal(StandaloneToolAuthenticationHandler.SchemeName,
            (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync())!.Name);
        var authorization = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        Assert.DoesNotContain(authorization.FallbackPolicy!.Requirements, x => x is DenyAnonymousAuthorizationRequirement);
        // 明示ポリシーは共有Cookieの実ユーザーだけを対象とし、単独開発では満たしません。
        Assert.Equal([SharedCookieContract.Scheme], authorization.GetPolicy("SalesSupportTool")!.AuthenticationSchemes);
        Assert.Equal([SharedCookieContract.Scheme], authorization.GetPolicy("SalesSupportAdmin")!.AuthenticationSchemes);
    }

    /// <summary>単独開発でも標準のData Protectionで発行したCSRFトークンを検証し、トークンのないPOSTを拒否します。</summary>
    [Fact]
    public async Task StandaloneKeepsCsrfValidation()
    {
        await using var provider = RegisterStandalone();
        var antiforgery = provider.GetRequiredService<IAntiforgery>();
        var issued = Https(provider, "GET");
        var tokens = antiforgery.GetAndStoreTokens(issued);
        var cookie = issued.Response.Headers.SetCookie.ToString().Split(';')[0];

        var valid = Https(provider, "POST");
        valid.Request.Headers.Cookie = cookie;
        valid.Request.Headers["X-CSRF-TOKEN"] = tokens.RequestToken;
        Assert.True(await antiforgery.IsRequestValidAsync(valid));

        var missing = Https(provider, "POST");
        missing.Request.Headers.Cookie = cookie;
        Assert.False(await antiforgery.IsRequestValidAsync(missing));
    }

    /// <summary>連携モードで共通設定ファイルがなければ、解決後のパスを示して起動を止め、単独開発へ切り替えません。</summary>
    [Fact]
    public void PortalModeRejectsMissingCommonConfiguration()
    {
        var exception = Assert.Throws<ConfigurationException>(() => Register(new() { ["SalesSupport:Application:ToolId"] = "T001" }));
        Assert.Contains(Path.Combine(root, "config", "salessupport.common.json"), exception.Message);
    }

    /// <summary>連携モードは既定位置の共通設定ファイルを読み、共有Cookieの認証済みユーザーを要求します。</summary>
    [Fact]
    public async Task PortalModeReadsDefaultCommonConfiguration()
    {
        var settings = new
        {
            ConnectionStrings = new { SalesSupport = "Data Source=verify;" },
            Portal = new { EnvironmentCode = "DEVELOPMENT" },
            SalesSupport = new
            {
                Application = new { Name = "検証", ToolId = "T001" },
                Portal = new { BaseUrl = "https://example.invalid/" },
                DataProtection = new { KeyDirectory = "../keys" }
            }
        };
        File.WriteAllText(Path.Combine(root, "config", "salessupport.common.json"), System.Text.Json.JsonSerializer.Serialize(settings));
        await using var provider = Register([]);
        var options = provider.GetRequiredService<IOptions<CommonOptions>>().Value;
        Assert.False(options.IsStandalone);
        Assert.Equal(ToolExecutionMode.Portal, options.ExecutionMode);
        Assert.Equal(SharedCookieContract.Scheme, (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync())!.Name);
        var fallback = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy!;
        Assert.Equal([SharedCookieContract.Scheme], fallback.AuthenticationSchemes);
        Assert.Contains(fallback.Requirements, x => x is DenyAnonymousAuthorizationRequirement);
    }

    /// <summary>起動プロファイルと同じホスト設定で、単独開発のツールのCommonを登録します。</summary>
    /// <returns>登録済みのサービスです。</returns>
    private ServiceProvider RegisterStandalone() => Register(new()
    {
        [ToolExecutionModeSettings.Key] = "Standalone",
        ["SalesSupport:Application:ToolId"] = "T001",
        ["SalesSupport:Application:Name"] = "検証ツール"
    });

    /// <summary>検証用のコンテンツルートとホスト設定でツールのCommonを登録します。</summary>
    /// <param name="values">ホスト設定です。</param>
    /// <returns>登録済みのサービスです。</returns>
    private ServiceProvider Register(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSalesSupportCommon(ApplicationKind.Tool, new StubEnvironment { ContentRootPath = contentRoot }, Host(values));
        return services.BuildServiceProvider();
    }

    /// <summary>HTTPSの要求を、登録済みサービスを使う要求として作成します。</summary>
    /// <param name="provider">要求で使うサービスです。</param>
    /// <param name="method">HTTPメソッドです。</param>
    /// <returns>検証用の要求です。</returns>
    private static DefaultHttpContext Https(IServiceProvider provider, string method)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Scheme = "https";
        context.Request.Method = method;
        return context;
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

    /// <summary>開発環境で単独開発を指定したツールの共通設定を作成します。</summary>
    /// <returns>ToolIdがT001の共通設定です。</returns>
    private static CommonOptions Standalone() => new()
    {
        Kind = ApplicationKind.Tool, EnvironmentCode = "DEVELOPMENT", ToolId = "T001", ExecutionMode = ToolExecutionMode.Standalone
    };

    /// <summary>連携モードのツールの共通設定を作成します。</summary>
    /// <returns>ToolIdがT001の共通設定です。</returns>
    private static CommonOptions Portal() => new() { Kind = ApplicationKind.Tool, EnvironmentCode = "DEVELOPMENT", ToolId = "T001" };

    /// <summary>秘密値を含まないホスト設定だけをメモリ上に組み立てます。</summary>
    /// <param name="values">設定する値です。</param>
    /// <returns>検証用のホスト設定です。</returns>
    private static IConfiguration Host(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    /// <summary>起動環境名とコンテンツルートだけを指定するホスト情報です。</summary>
    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
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
