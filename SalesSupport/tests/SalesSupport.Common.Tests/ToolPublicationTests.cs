using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Data;
using SalesSupport.Common.Entities.Authentication;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>単独開発の環境制約と、通常運用のDB公開状態による拒否を確認します。</summary>
public sealed class ToolPublicationTests
{
    /// <summary>DB確認の省略は両方の環境区分が開発の場合だけ許可します。</summary>
    [Theory]
    [InlineData("DEVELOPMENT", "Development", false)]
    [InlineData("PRODUCTION", "Development", true)]
    [InlineData("DEVELOPMENT", "Production", true)]
    [InlineData("DEVELOPMENT", "Staging", true)]
    [InlineData("", "Development", true)]
    public void StandaloneRequiresBothDevelopmentEnvironments(string environmentCode, string hostEnvironment, bool rejected)
    {
        var configuration = Configuration(environmentCode, "false");
        var host = new StubEnvironment { EnvironmentName = hostEnvironment };
        if (rejected) Assert.Throws<ConfigurationException>(() => ToolPublicationSettings.ReadCheckPublicationStatus(configuration, ApplicationKind.Tool, host));
        else Assert.False(ToolPublicationSettings.ReadCheckPublicationStatus(configuration, ApplicationKind.Tool, host));
    }

    /// <summary>未設定時はDB確認を行い、不正値やホスト不明で省略を指定した場合は拒否します。</summary>
    [Fact]
    public void MissingSettingChecksDatabaseAndInvalidSettingIsRejected()
    {
        Assert.True(ToolPublicationSettings.ReadCheckPublicationStatus(Configuration("PRODUCTION", null), ApplicationKind.Tool, null));
        Assert.True(ToolPublicationSettings.ReadCheckPublicationStatus(Configuration("PRODUCTION", "true"), ApplicationKind.Tool, null));
        Assert.Throws<ConfigurationException>(() => ToolPublicationSettings.ReadCheckPublicationStatus(Configuration("DEVELOPMENT", "invalid"), ApplicationKind.Tool, new StubEnvironment()));
        Assert.Throws<ConfigurationException>(() => ToolPublicationSettings.ReadCheckPublicationStatus(Configuration("DEVELOPMENT", "false"), ApplicationKind.Tool, null));
    }

    /// <summary>共通設定を共有してもPortalのDB確認は省略できません。</summary>
    [Fact]
    public void PortalAlwaysChecksDatabase() => Assert.True(
        ToolPublicationSettings.ReadCheckPublicationStatus(Configuration("DEVELOPMENT", "false"), ApplicationKind.Portal, new StubEnvironment()));

    /// <summary>単独開発では対象ツールだけをDB未接続で利用でき、管理・他ツールへの権限は得られません。</summary>
    [Fact]
    public async Task StandaloneAllowsOnlyConfiguredToolWithoutDatabaseOrUser()
    {
        var options = Options.Create(new CommonOptions { Kind = ApplicationKind.Tool, EnvironmentCode = "DEVELOPMENT", ToolId = "T001", CheckToolPublicationStatus = false });
        var evaluator = new AccessEvaluator(new CurrentUserAccessor(), new RejectSettings(), new RejectFactory(), options);
        Assert.True((await evaluator.EvaluateAsync(new(AccessPurpose.ToolUse, "T001"))).Allowed);
        Assert.False((await evaluator.EvaluateAsync(new(AccessPurpose.ToolUse, "OTHER"))).Allowed);
        Assert.False((await evaluator.EvaluateAsync(new(AccessPurpose.ToolManage, "T001"))).Allowed);
        Assert.False((await evaluator.EvaluateAsync(new(AccessPurpose.Site))).Allowed);
        Assert.Null(await new DatabaseSettingsReader(new RejectFactory(), options).GetBusinessDateAsync());
        var connections = new ConnectionStringProvider(Configuration("DEVELOPMENT", "false"), requireConnectionString: false);
        Assert.Throws<ConfigurationException>(() => connections.SalesSupportDatabase);
    }

    /// <summary>本番区分では省略フラグだけで未認証のツール利用を許可しません。</summary>
    [Fact]
    public async Task ProductionDoesNotBypassAuthentication()
    {
        var options = Options.Create(new CommonOptions { Kind = ApplicationKind.Tool, EnvironmentCode = "PRODUCTION", ToolId = "T001", CheckToolPublicationStatus = false });
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
        var root = new InMemoryDatabaseRoot();
        var name = Guid.NewGuid().ToString("N");
        await using (var seed = new SeedContext(new DbContextOptionsBuilder<SeedContext>().UseInMemoryDatabase(name, root).Options))
        {
            seed.Add(new RoleAccessRecord { RoleCode = roleCode });
            if (toolStatus is not null) seed.Add(new ToolAccessRecord { ToolId = "T001", ToolType = "WEB", Status = toolStatus });
            if (assigned) seed.Add(new ToolRoleAccessRecord { ToolId = "T001", RoleCode = roleCode });
            await seed.SaveChangesAsync();
        }
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(Guid.NewGuid(), "検証用", roleCode));
        var factory = new MemoryFactory(new DbContextOptionsBuilder<CommonDbContext>().UseInMemoryDatabase(name, root).Options);
        var settings = new DatabaseSettingsReader(factory, Options.Create(new CommonOptions { EnvironmentCode = "PRODUCTION" }));
        await using (var seed = new SeedContext(new DbContextOptionsBuilder<SeedContext>().UseInMemoryDatabase(name, root).Options))
        {
            seed.Add(new SalesSupport.Common.Entities.Configuration.SystemSetting { SettingCategory = "SITE", SettingKey = "PUBLICATION_STATUS", SettingValue = siteStatus });
            await seed.SaveChangesAsync();
        }
        var evaluator = new AccessEvaluator(current, settings, factory, Options.Create(new CommonOptions { Kind = ApplicationKind.Tool, EnvironmentCode = "PRODUCTION", ToolId = "T001" }));
        var decision = await evaluator.EvaluateAsync(new(AccessPurpose.ToolUse, "T001"));
        Assert.Equal(expectedStatus, decision.StatusCode);
        Assert.Equal(expectedStatus == 200, decision.Allowed);
    }

    /// <summary>秘密値を含まない設定だけをメモリ上に組み立てます。</summary>
    private static IConfiguration Configuration(string environmentCode, string? check) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Portal:EnvironmentCode"] = environmentCode, ["SalesSupport:Tool:CheckPublicationStatus"] = check }).Build();

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
        /// <summary>単独開発でDBを作成してはいけません。</summary>
        public CommonDbContext CreateDbContext() => throw new InvalidOperationException("単独開発でDBを参照しました。");
    }

    /// <summary>単独開発ではサイトのDB設定も取得しません。</summary>
    private sealed class RejectSettings : ISystemSettingsReader
    {
        /// <summary>サイト公開状態のDB取得を検出します。</summary>
        public Task<string> GetPublicationStatusAsync(CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>非公開案内のDB取得を検出します。</summary>
        public Task<string> GetPrivateMessageAsync(CancellationToken ct = default) => throw new InvalidOperationException();
        /// <summary>業務日付のDB取得を検出します。</summary>
        public Task<DateOnly?> GetBusinessDateAsync(CancellationToken ct = default) => throw new InvalidOperationException();
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
