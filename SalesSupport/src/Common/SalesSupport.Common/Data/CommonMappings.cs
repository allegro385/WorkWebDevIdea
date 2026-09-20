using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SalesSupport.Common.Entities;
using SalesSupport.Common.Entities.Authentication;
using SalesSupport.Common.Entities.Configuration;
using SalesSupport.Common.Entities.Identity;

namespace SalesSupport.Common.Data;

/// <summary>CommonとPortalで共有する既存SQLへのマッピングです。</summary>
public static class CommonMappings
{
    /// <summary>共通読取りモデルを構成します。</summary>
    public static void ConfigureReadModels(ModelBuilder model)
    {
        var codeMasterEntry = model.Entity<CodeMasterEntry>();
        codeMasterEntry.ToTable("CodeMaster", "portal");
        codeMasterEntry.HasKey(x => new { x.CodeType, x.CodeValue });
        codeMasterEntry.Property(x => x.CodeType).HasMaxLength(50).IsUnicode(false);
        codeMasterEntry.Property(x => x.CodeValue).HasMaxLength(20).IsUnicode(false);
        codeMasterEntry.Property(x => x.CodeName).HasMaxLength(100);
        codeMasterEntry.Property(x => x.ColorCode).HasMaxLength(7).IsUnicode(false);
        codeMasterEntry.ConfigureAuditColumns("CodeMaster");
        var systemSetting = model.Entity<SystemSetting>();
        systemSetting.ToTable("SystemSettings", "portal");
        systemSetting.HasKey(x => new { x.SettingCategory, x.SettingKey });
        systemSetting.Property(x => x.SettingCategory).HasMaxLength(50).IsUnicode(false);
        systemSetting.Property(x => x.SettingKey).HasMaxLength(50).IsUnicode(false);
        systemSetting.Property(x => x.SettingName).HasMaxLength(100);
        systemSetting.Property(x => x.SettingValue).HasMaxLength(2000);
        systemSetting.Property(x => x.Description).HasMaxLength(1000);
        systemSetting.ConfigureAuditColumns("SystemSettings");
        var uploadPolicy = model.Entity<UploadPolicy>();
        uploadPolicy.ToTable("FileUploadPolicies", "portal");
        uploadPolicy.HasKey(x => x.PolicyId);
        uploadPolicy.Property(x => x.ScopeType).HasMaxLength(20).IsUnicode(false);
        uploadPolicy.Property(x => x.ToolId).HasMaxLength(20).IsUnicode(false);
        uploadPolicy.Property(x => x.PurposeCode).HasMaxLength(30).IsUnicode(false);
        uploadPolicy.ConfigureAuditColumns("FileUploadPolicies");
        var uploadPolicyExtension = model.Entity<UploadPolicyExtension>();
        uploadPolicyExtension.ToTable("FileUploadPolicyExtensions", "portal");
        uploadPolicyExtension.HasKey(x => new { x.PolicyId, x.Extension });
        uploadPolicyExtension.Property(x => x.Extension).HasMaxLength(20).IsUnicode(false);
        uploadPolicyExtension.ConfigureAuditColumns("FileUploadPolicyExtensions");
        var errorCodeEntry = model.Entity<ErrorCodeEntry>();
        errorCodeEntry.ToTable("ErrorCodes", "portal");
        errorCodeEntry.HasKey(x => new { x.ToolId, x.ErrorNo });
        errorCodeEntry.Property(x => x.ToolId).HasMaxLength(20).IsUnicode(false);
        errorCodeEntry.Property(x => x.ErrorCode).HasMaxLength(100).IsUnicode(false);
        errorCodeEntry.Property(x => x.DisplayMessage).HasMaxLength(2000);
        errorCodeEntry.Property(x => x.ErrorLevel).HasMaxLength(20).IsUnicode(false);
        errorCodeEntry.ConfigureAuditColumns("ErrorCodes");
        var userAccessRecord = model.Entity<UserAccessRecord>();
        userAccessRecord.ToTable("AspNetUsers", "portal");
        userAccessRecord.HasKey(x => x.UserId);
        userAccessRecord.Property(x => x.DisplayName).HasMaxLength(100);
        userAccessRecord.Property(x => x.RoleCode).HasMaxLength(20).IsUnicode(false);
        var toolAccessRecord = model.Entity<ToolAccessRecord>();
        toolAccessRecord.ToTable("Tools", "portal");
        toolAccessRecord.HasKey(x => x.ToolId);
        toolAccessRecord.Property(x => x.ToolId).HasMaxLength(20).IsUnicode(false);
        toolAccessRecord.Property(x => x.ToolName).HasMaxLength(200);
        toolAccessRecord.Property(x => x.ToolType).HasMaxLength(20).IsUnicode(false);
        toolAccessRecord.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
    }

    /// <summary>監査列をDB生成・同時更新判定用として設定します。</summary>
    public static void ConfigureAuditColumns<T>(this EntityTypeBuilder<T> entity, string table) where T : AuditedEntity
    {
        entity.ToTable(table, "portal", options => options.UseSqlOutputClause(false));
        entity.Property(x => x.UpdateCount).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        entity.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").ValueGeneratedOnAddOrUpdate();
        entity.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").ValueGeneratedOnAddOrUpdate();
        entity.Property(x => x.CreatedBy).HasMaxLength(128).ValueGeneratedOnAddOrUpdate();
        entity.Property(x => x.UpdatedBy).HasMaxLength(128).ValueGeneratedOnAddOrUpdate();
        foreach (var name in new[] { "UpdateCount", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" })
        {
            var property = entity.Property(name).Metadata;
            property.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
            property.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }
    }

    /// <summary>PortalのIdentity基底マッピングの後に呼び、既存4テーブルへ対応させます。</summary>
    public static void ConfigureIdentity(ModelBuilder model)
    {
        var user = model.Entity<ApplicationUser>();
        user.ToTable("AspNetUsers", "portal");
        user.Property(x => x.Id).HasColumnName("UserId").HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();
        user.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        user.Property(x => x.RoleCode).HasMaxLength(20).IsUnicode(false).IsRequired();
        user.Property(x => x.LastAccessAt).HasColumnType("datetime2(3)");
        user.Property(x => x.SecurityStamp).IsRequired();
        user.Property(x => x.ConcurrencyStamp).IsRequired().IsConcurrencyToken();
        user.Property(x => x.UserName).HasMaxLength(256).IsRequired();
        user.Property(x => x.NormalizedUserName).HasMaxLength(256).IsRequired();
        user.Property(x => x.Email).HasMaxLength(256).IsRequired();
        user.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        user.HasIndex(x => x.NormalizedUserName).IsUnique().HasDatabaseName("UX_AspNetUsers_NormalizedUserName");
        user.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("UX_AspNetUsers_NormalizedEmail");
        model.Entity<IdentityUserClaim<Guid>>().ToTable("AspNetUserClaims", "portal");
        model.Entity<IdentityUserLogin<Guid>>().ToTable("AspNetUserLogins", "portal");
        model.Entity<IdentityUserToken<Guid>>().ToTable("AspNetUserTokens", "portal");
        model.Entity<IdentityUserLogin<Guid>>().Property(x => x.LoginProvider).HasMaxLength(128);
        model.Entity<IdentityUserLogin<Guid>>().Property(x => x.ProviderKey).HasMaxLength(128);
        model.Entity<IdentityUserToken<Guid>>().Property(x => x.LoginProvider).HasMaxLength(128);
        model.Entity<IdentityUserToken<Guid>>().Property(x => x.Name).HasMaxLength(128);
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var foreignKey in entity.GetForeignKeys().Where(x => x.PrincipalEntityType.ClrType == typeof(ApplicationUser)))
                foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
    }
}
