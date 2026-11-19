using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SalesSupport.SampleEstimate.Web.Data;
using Xunit;

namespace SalesSupport.SampleEstimate.Tests;

/// <summary>実DBへ接続せず、サンプル専用EFモデルとSQLの主要契約を確認します。</summary>
public sealed class SampleEstimateMappingTests
{
    /// <summary>更新回数と送信IDの一意索引が同時更新・二重送信を抑える設定か、監査列がCommonの共通マッピングで設定されているかを確認します。</summary>
    [Fact]
    public void RecordUsesOneTableUpdateCountAndUniqueSubmission()
    {
        var options = new DbContextOptionsBuilder<SampleEstimateDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        using var db = new SampleEstimateDbContext(options);
        var entity = db.Model.FindEntityType(typeof(EstimateRecord));
        Assert.NotNull(entity);

        Assert.Equal("dbo", entity.GetSchema());
        Assert.Equal("SampleEstimateRecords", entity.GetTableName());
        Assert.True(entity.FindProperty(nameof(EstimateRecord.UpdateCount))!.IsConcurrencyToken);
        Assert.True(entity.FindProperty(nameof(EstimateRecord.UpdateCount))!.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate);
        // 監査列はCommonの `ConfigureAuditColumns` が設定するため、桁数とDB生成の扱いもそこで揃います。
        Assert.Equal(PropertySaveBehavior.Ignore, entity.FindProperty(nameof(EstimateRecord.UpdateCount))!.GetBeforeSaveBehavior());
        Assert.Equal(PropertySaveBehavior.Ignore, entity.FindProperty(nameof(EstimateRecord.UpdatedAt))!.GetAfterSaveBehavior());
        Assert.Equal(128, entity.FindProperty(nameof(EstimateRecord.CreatedBy))!.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == nameof(EstimateRecord.SubmissionId));
        Assert.Single(db.Model.GetEntityTypes());
    }

    /// <summary>一覧の更新日時は監査列ではなく、アプリケーションが保存する業務列EditedAtを使うことを確認します。</summary>
    [Fact]
    public void ListUsesBusinessEditedAtColumn()
    {
        var options = new DbContextOptionsBuilder<SampleEstimateDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        using var db = new SampleEstimateDbContext(options);
        var editedAt = db.Model.FindEntityType(typeof(EstimateRecord))!.FindProperty(nameof(EstimateRecord.EditedAt));

        Assert.NotNull(editedAt);
        Assert.Equal(ValueGenerated.Never, editedAt.ValueGenerated);
        Assert.Equal("datetime2(3)", editedAt.GetColumnType());
        var summaryProperties = typeof(SalesSupport.SampleEstimate.Web.Services.EstimateRecordSummary).GetProperties().Select(x => x.Name).ToList();
        Assert.Contains(nameof(EstimateRecord.EditedAt), summaryProperties);
        Assert.DoesNotContain(nameof(EstimateRecord.UpdatedAt), summaryProperties);
    }
}
