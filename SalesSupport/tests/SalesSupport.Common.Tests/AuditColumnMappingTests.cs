using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SalesSupport.Common.Data;
using SalesSupport.Common.Entities;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>監査列の共通マッピングが、Portal以外のスキーマへ置くツール固有表にも同じ設定を与えることを確認します。</summary>
/// <remarks>モデル定義だけを確認し、DBへは接続しません。</remarks>
public sealed class AuditColumnMappingTests
{
    /// <summary>スキーマを指定しない場合はportalへ対応付け、Portal・Commonの既存表の配置を変えません。</summary>
    [Fact]
    public void DefaultSchemaIsPortal()
    {
        using var db = new PortalSchemaContext();
        var entity = db.Model.FindEntityType(typeof(AuditedRecord))!;

        Assert.Equal("portal", entity.GetSchema());
        Assert.Equal("Samples", entity.GetTableName());
    }

    /// <summary>ツール固有表のためにportal以外のスキーマを指定できます。</summary>
    [Fact]
    public void ToolSchemaIsApplied()
    {
        using var db = new ToolSchemaContext();
        var entity = db.Model.FindEntityType(typeof(AuditedRecord))!;

        Assert.Equal("dbo", entity.GetSchema());
        Assert.Equal("Samples", entity.GetTableName());
    }

    /// <summary>スキーマを指定しても、監査列はDB生成・同時更新トークン・アプリ入力の無視で揃います。</summary>
    [Fact]
    public void ToolSchemaKeepsAuditColumnContract()
    {
        using var db = new ToolSchemaContext();
        var entity = db.Model.FindEntityType(typeof(AuditedRecord))!;

        var updateCount = entity.FindProperty(nameof(AuditedEntity.UpdateCount))!;
        Assert.True(updateCount.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, updateCount.ValueGenerated);
        Assert.Equal("datetime2(3)", entity.FindProperty(nameof(AuditedEntity.CreatedAt))!.GetColumnType());
        Assert.Equal("datetime2(3)", entity.FindProperty(nameof(AuditedEntity.UpdatedAt))!.GetColumnType());
        Assert.Equal(128, entity.FindProperty(nameof(AuditedEntity.CreatedBy))!.GetMaxLength());
        Assert.Equal(128, entity.FindProperty(nameof(AuditedEntity.UpdatedBy))!.GetMaxLength());
        // 監査列はDBのDEFAULTとトリガーが設定するため、登録・更新のどちらでもアプリの値を送りません。
        foreach (var name in new[] { nameof(AuditedEntity.UpdateCount), nameof(AuditedEntity.CreatedAt), nameof(AuditedEntity.CreatedBy), nameof(AuditedEntity.UpdatedAt), nameof(AuditedEntity.UpdatedBy) })
        {
            var property = entity.FindProperty(name)!;
            Assert.Equal(PropertySaveBehavior.Ignore, property.GetBeforeSaveBehavior());
            Assert.Equal(PropertySaveBehavior.Ignore, property.GetAfterSaveBehavior());
        }
    }

    /// <summary>監査列を持つ検証専用のEntityです。</summary>
    private sealed class AuditedRecord : AuditedEntity
    {
        /// <summary>主キーです。</summary>
        public Guid RecordId { get; set; }
    }

    /// <summary>モデル定義だけを読む検証用Contextです。接続文字列は解析だけに使い、DBへは接続しません。</summary>
    private abstract class AuditedRecordContext : DbContext
    {
        /// <summary>SQL Serverのモデル規約を使う設定です。</summary>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true");

        /// <summary>主キーだけを定義し、監査列は派生クラスの指定で対応付けます。</summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var entity = modelBuilder.Entity<AuditedRecord>();
            entity.HasKey(x => x.RecordId);
            Configure(entity);
        }

        /// <summary>検証したいスキーマ指定で共通の監査列マッピングを適用します。</summary>
        /// <param name="entity">対応付ける検証専用Entityです。</param>
        protected abstract void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AuditedRecord> entity);
    }

    /// <summary>スキーマを指定しない既定の呼出しです。</summary>
    private sealed class PortalSchemaContext : AuditedRecordContext
    {
        /// <summary>テーブル名だけを渡します。</summary>
        protected override void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AuditedRecord> entity) => entity.ConfigureAuditColumns("Samples");
    }

    /// <summary>ツール固有表と同じくdboを指定する呼出しです。</summary>
    private sealed class ToolSchemaContext : AuditedRecordContext
    {
        /// <summary>テーブル名とスキーマを渡します。</summary>
        protected override void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<AuditedRecord> entity) => entity.ConfigureAuditColumns("Samples", "dbo");
    }
}
