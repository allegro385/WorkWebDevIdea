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
            (typeof(Role), "Roles"), (typeof(ToolRole), "ToolRoles"),
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
        var portalTypes = new[] { typeof(ApplicationUser), typeof(Tool), typeof(ToolRole), typeof(UserPreference), typeof(UserToolFavorite), typeof(ToolVersionHistory), typeof(ToolFile), typeof(Notice), typeof(FaqItem), typeof(Inquiry) };
        var foreignKeys = portalTypes.SelectMany(type => context.Model.FindEntityType(type)!.GetForeignKeys()).ToArray();
        Assert.NotEmpty(foreignKeys);
        Assert.All(foreignKeys, foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
    }

    /// <summary>ロール参照・ツール割当て・カテゴリ並び順廃止をモデルで確認します。</summary>
    [Fact]
    public void ModelUsesRoleForeignKeysAndToolOnlyOrdering()
    {
        using var context = CreateContext();
        var user = context.Model.FindEntityType(typeof(ApplicationUser))!;
        var toolRole = context.Model.FindEntityType(typeof(ToolRole))!;
        Assert.Contains(user.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(Role));
        Assert.Contains(toolRole.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(Role));
        Assert.Contains(toolRole.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(Tool));
        Assert.Null(context.Model.FindEntityType(typeof(ToolCategory))!.FindProperty("SortOrder"));
        Assert.Equal(2, toolRole.FindPrimaryKey()!.Properties.Count);
    }

    /// <summary>DB接続なしでSQL Server向けモデルを生成します。</summary>
    private static PortalDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SalesSupportModelOnly;Trusted_Connection=True")
            .Options;
        return new PortalDbContext(options);
    }

    /// <summary>内部Guidを維持し、外部ログインIDと通知既定OFFをDDLへ対応させます。</summary>
    [Fact]
    public void ExternalLoginAndNotificationColumnsMatchDdl()
    {
        using var context = CreateContext();
        var user = context.Model.FindEntityType(typeof(ApplicationUser))!;
        var table = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table("AspNetUsers", "portal");
        Assert.Equal("UserId", user.FindProperty(nameof(ApplicationUser.Id))!.GetColumnName(table));
        Assert.Equal("LoginId", user.FindProperty(nameof(ApplicationUser.UserName))!.GetColumnName(table));
        Assert.Equal("NormalizedLoginId", user.FindProperty(nameof(ApplicationUser.NormalizedUserName))!.GetColumnName(table));
        var preference = context.Model.FindEntityType(typeof(UserPreference))!;
        Assert.Equal(false, preference.FindProperty(nameof(UserPreference.SystemNoticeMailEnabled))!.GetDefaultValue());
        Assert.Equal(false, preference.FindProperty(nameof(UserPreference.FavoriteToolNoticeMailEnabled))!.GetDefaultValue());
        Assert.Equal(false, context.Model.FindEntityType(typeof(Role))!.FindProperty(nameof(Role.NoticeMailEnabled))!.GetDefaultValue());
    }
}
