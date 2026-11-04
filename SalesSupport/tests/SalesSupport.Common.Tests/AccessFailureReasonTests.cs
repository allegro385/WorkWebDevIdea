using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Data;
using SalesSupport.Common.Entities.Authentication;
using SalesSupport.Common.Entities.Configuration;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>拒否理由が実際の原因を示し、未登録のツールをサイト非公開と混同しないことを確認します。</summary>
public sealed class AccessFailureReasonTests
{
    /// <summary>サイトが公開中なら、未登録のツールはツールの利用不可として404で拒否します。</summary>
    [Fact]
    public async Task UnregisteredToolIsNotReportedAsSitePrivate()
    {
        var decision = await EvaluateAsync("PUBLIC", "A");

        Assert.Equal(new AccessDecision(false, 404, "TOOL_UNAVAILABLE"), decision);
    }

    /// <summary>サイトが非公開なら、ツールの登録有無にかかわらず一般ロールをサイト非公開として拒否します。</summary>
    [Fact]
    public async Task PrivateSiteIsCheckedBeforeTool()
    {
        var decision = await EvaluateAsync("PRIVATE", "A");

        Assert.Equal(new AccessDecision(false, 403, "SITE_PRIVATE"), decision);
    }

    /// <summary>管理者は非公開のサイトへ入場できるため、未登録のツールはツールの利用不可になります。</summary>
    [Fact]
    public async Task AdministratorOnPrivateSiteSeesToolReason()
    {
        var decision = await EvaluateAsync("PRIVATE", "ADMIN");

        Assert.Equal(new AccessDecision(false, 404, "TOOL_UNAVAILABLE"), decision);
    }

    /// <summary>ツールを登録していないDBで、利用判定だけを実行します。</summary>
    /// <param name="publication">サイトの公開状態です。</param>
    /// <param name="roleCode">利用者のロールです。</param>
    /// <returns>判定結果です。</returns>
    private static async Task<AccessDecision> EvaluateAsync(string publication, string roleCode)
    {
        var root = new InMemoryDatabaseRoot();
        var name = Guid.NewGuid().ToString("N");
        await using (var seed = new SeedContext(new DbContextOptionsBuilder<SeedContext>().UseInMemoryDatabase(name, root).Options))
        {
            seed.Add(new RoleAccessRecord { RoleCode = roleCode });
            seed.Add(new SystemSetting { SettingCategory = "SITE", SettingKey = "PUBLICATION_STATUS", SettingValue = publication });
            await seed.SaveChangesAsync();
        }
        var factory = new MemoryFactory(new DbContextOptionsBuilder<CommonDbContext>().UseInMemoryDatabase(name, root).Options);
        var options = Options.Create(new CommonOptions { Kind = ApplicationKind.Tool, EnvironmentCode = "PRODUCTION", ToolId = "T001" });
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(Guid.NewGuid(), "利用者", roleCode));
        return await new AccessEvaluator(current, new DatabaseSettingsReader(factory, options), factory, options)
            .EvaluateAsync(new(AccessPurpose.ToolUse, "T001"));
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
