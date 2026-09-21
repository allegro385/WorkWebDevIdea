using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>PortalのEF Coreモデルが既存DDLとの主要契約を維持することを確認します。</summary>
public sealed class PortalDbContextTests
{
    /// <summary>Identityと全Portal業務Entityがportalスキーマへ割り当てられることを確認します。</summary>
    [Fact]
    public void ModelMapsIdentityAndPortalEntitiesToPortalSchema()
    {
        using var context = CreateContext();
        var expected = new[]
        {
            (typeof(ApplicationUser), "AspNetUsers"), (typeof(Tool), "Tools"), (typeof(ToolCategory), "ToolCategories"),
            (typeof(UserPreference), "UserPreferences"), (typeof(UserToolFavorite), "UserToolFavorites"),
            (typeof(ToolVersionHistory), "ToolVersionHistories"), (typeof(ToolFile), "ToolFiles"),
            (typeof(Notice), "Notices"), (typeof(FaqCategory), "FaqCategories"), (typeof(FaqItem), "FaqItems"),
            (typeof(Inquiry), "Inquiries")
        };

        foreach (var (type, table) in expected)
        {
            var entity = context.Model.FindEntityType(type);
            Assert.NotNull(entity);
            Assert.Equal("portal", entity.GetSchema());
            Assert.Equal(table, entity.GetTableName());
        }
    }

    /// <summary>監査列とIdentityの競合列が同時更新トークンとして構成されることを確認します。</summary>
    [Fact]
    public void ModelConfiguresConcurrencyTokens()
    {
        using var context = CreateContext();
        Assert.True(context.Model.FindEntityType(typeof(Tool))!.FindProperty(nameof(Tool.UpdateCount))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(ApplicationUser))!.FindProperty(nameof(ApplicationUser.ConcurrencyStamp))!.IsConcurrencyToken);
    }

    /// <summary>Portalの外部キーが親レコードを連鎖削除しないことを確認します。</summary>
    [Fact]
    public void ModelUsesNoActionForPortalForeignKeys()
    {
        using var context = CreateContext();
        var portalTypes = new[] { typeof(Tool), typeof(UserPreference), typeof(UserToolFavorite), typeof(ToolVersionHistory), typeof(ToolFile), typeof(Notice), typeof(FaqItem), typeof(Inquiry) };
        var foreignKeys = portalTypes.SelectMany(type => context.Model.FindEntityType(type)!.GetForeignKeys()).ToArray();
        Assert.NotEmpty(foreignKeys);
        Assert.All(foreignKeys, foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
    }

    /// <summary>DB接続なしでSQL Server向けモデルを生成します。</summary>
    private static PortalDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SalesSupportModelOnly;Trusted_Connection=True")
            .Options;
        return new PortalDbContext(options);
    }
}
