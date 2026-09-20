using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Entities.Authentication;
using SalesSupport.Common.Entities.Configuration;

namespace SalesSupport.Common.Data;

/// <summary>共通設定・認証情報を追跡せず参照する専用Contextです。</summary>
public sealed class CommonDbContext(DbContextOptions<CommonDbContext> options) : DbContext(options)
{
    public DbSet<CodeMasterEntry> Codes => Set<CodeMasterEntry>();
    public DbSet<SystemSetting> Settings => Set<SystemSetting>();
    public DbSet<UploadPolicy> UploadPolicies => Set<UploadPolicy>();
    public DbSet<UploadPolicyExtension> UploadExtensions => Set<UploadPolicyExtension>();
    public DbSet<ErrorCodeEntry> ErrorCodes => Set<ErrorCodeEntry>();
    public DbSet<UserAccessRecord> Users => Set<UserAccessRecord>();
    public DbSet<ToolAccessRecord> Tools => Set<ToolAccessRecord>();

    /// <summary>既存DDLへマッピングし、読取りに必要な列を定義します。</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        CommonMappings.ConfigureReadModels(modelBuilder);
    }

    /// <summary>共通参照Contextからの更新を拒否します。</summary>
    public override int SaveChanges() => throw new InvalidOperationException("共通Contextは読取り専用です。");
    /// <summary>追跡状態にかかわらず更新を拒否します。</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw new InvalidOperationException("共通Contextは読取り専用です。");
    /// <summary>非同期更新を拒否します。</summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("共通Contextは読取り専用です。");
    /// <summary>追跡状態にかかわらず非同期更新を拒否します。</summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => throw new InvalidOperationException("共通Contextは読取り専用です。");
}
