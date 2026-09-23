using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SalesSupport.SampleEstimate.Web.Data;

/// <summary>サンプル専用の1表だけを扱い、PortalとCommonの表を再定義しません。</summary>
public sealed class SampleEstimateDbContext(DbContextOptions<SampleEstimateDbContext> options) : DbContext(options)
{
    /// <summary>サンプル見積案件です。</summary>
    public DbSet<EstimateRecord> EstimateRecords => Set<EstimateRecord>();

    /// <summary>開発用SQLの列型、索引、同時更新トークンと対応付けます。</summary>
    /// <param name="modelBuilder">EF Coreのモデル定義です。</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var record = modelBuilder.Entity<EstimateRecord>();
        record.ToTable("SampleEstimateRecords", "dbo", table => table.UseSqlOutputClause(false));
        record.HasKey(x => x.RecordId);
        record.HasIndex(x => x.SubmissionId).IsUnique();
        record.HasIndex(x => new { x.OwnerUserId, x.AppliedOn });
        record.Property(x => x.ProjectName).HasMaxLength(60).IsRequired();
        record.Property(x => x.CategoryCode).HasMaxLength(20).IsUnicode(false).IsRequired();
        record.Property(x => x.Subtotal).HasPrecision(18, 0);
        record.Property(x => x.DiscountAmount).HasPrecision(18, 0);
        record.Property(x => x.Total).HasPrecision(18, 0);
        record.Property(x => x.UpdateCount).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        record.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").ValueGeneratedOnAddOrUpdate();
        record.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").ValueGeneratedOnAddOrUpdate();
        record.Property(x => x.CreatedBy).HasMaxLength(128).ValueGeneratedOnAddOrUpdate();
        record.Property(x => x.UpdatedBy).HasMaxLength(128).ValueGeneratedOnAddOrUpdate();
        // 監査列はDBトリガーが設定します。アプリ入力から値を受け取りません。
        foreach (var name in new[] { "UpdateCount", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" })
        {
            var property = record.Property(name).Metadata;
            property.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
            property.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }
    }
}
