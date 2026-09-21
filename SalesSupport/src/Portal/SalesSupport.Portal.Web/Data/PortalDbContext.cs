using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Data;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Portal.Web.Entities;

namespace SalesSupport.Portal.Web.Data;

/// <summary>Identityの4テーブルとPortal業務テーブルを同じ保存単位で扱います。</summary>
public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<ToolCategory> ToolCategories => Set<ToolCategory>();
    public DbSet<Tool> Tools => Set<Tool>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<UserToolFavorite> UserToolFavorites => Set<UserToolFavorite>();
    public DbSet<ToolVersionHistory> ToolVersionHistories => Set<ToolVersionHistory>();
    public DbSet<ToolFile> ToolFiles => Set<ToolFile>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<FaqCategory> FaqCategories => Set<FaqCategory>();
    public DbSet<FaqItem> FaqItems => Set<FaqItem>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();

    /// <summary>既存DDLに合わせてIdentityとPortal業務Entityを構成します。</summary>
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        CommonMappings.ConfigureIdentity(model);
        ConfigureToolModels(model);
        ConfigureUserModels(model);
        ConfigureContentModels(model);
    }

    /// <summary>ツール、履歴および提供ファイルの列と参照関係を構成します。</summary>
    private static void ConfigureToolModels(ModelBuilder model)
    {
        var category = model.Entity<ToolCategory>();
        category.HasKey(x => x.CategoryId);
        category.Property(x => x.CategoryId).ValueGeneratedOnAdd();
        category.Property(x => x.CategoryName).HasMaxLength(100).IsRequired();
        category.ConfigureAuditColumns("ToolCategories");

        var tool = model.Entity<Tool>();
        tool.HasKey(x => x.ToolId);
        tool.Property(x => x.ToolId).HasMaxLength(20).IsUnicode(false);
        tool.Property(x => x.ToolName).HasMaxLength(200).IsRequired();
        tool.Property(x => x.ToolSummary).HasMaxLength(1000);
        tool.Property(x => x.Remarks).HasMaxLength(2000);
        tool.Property(x => x.ToolType).HasMaxLength(20).IsUnicode(false);
        tool.Property(x => x.WebAppUrl).HasMaxLength(2048);
        tool.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
        tool.HasOne<ToolCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
        tool.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        tool.ConfigureAuditColumns("Tools");

        var history = model.Entity<ToolVersionHistory>();
        history.HasKey(x => x.ToolHistoryId);
        history.Property(x => x.ToolHistoryId).ValueGeneratedOnAdd();
        history.Property(x => x.ToolId).HasMaxLength(20).IsUnicode(false);
        history.Property(x => x.Version).HasMaxLength(50).IsRequired();
        history.Property(x => x.ChangeDescription).HasMaxLength(2000).IsRequired();
        history.Property(x => x.ReleasedAt).HasColumnType("date");
        history.HasIndex(x => new { x.ToolId, x.Version }).IsUnique().HasDatabaseName("UQ_ToolVersionHistories_Tool_Version");
        history.HasIndex(x => x.ToolId).IsUnique().HasFilter("[IsCurrent] = 1").HasDatabaseName("UX_ToolVersionHistories_Current");
        history.HasOne<Tool>().WithMany().HasForeignKey(x => x.ToolId).OnDelete(DeleteBehavior.NoAction);
        history.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ModifiedByUserId).OnDelete(DeleteBehavior.NoAction);
        history.ConfigureAuditColumns("ToolVersionHistories");

        var file = model.Entity<ToolFile>();
        file.HasKey(x => x.FileId);
        file.Property(x => x.FileId).ValueGeneratedOnAdd();
        file.Property(x => x.ToolId).HasMaxLength(20).IsUnicode(false);
        file.Property(x => x.FileCategory).HasMaxLength(20).IsUnicode(false);
        file.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        file.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        file.Property(x => x.RelativePath).HasMaxLength(500).IsRequired();
        file.Property(x => x.Extension).HasMaxLength(20).IsUnicode(false);
        file.Property(x => x.UploadedAt).HasColumnType("datetime2(3)");
        file.HasIndex(x => x.RelativePath).IsUnique().HasDatabaseName("UQ_ToolFiles_RelativePath");
        file.HasIndex(x => x.ToolId).IsUnique().HasFilter("[FileCategory] = 'APP'").HasDatabaseName("UX_ToolFiles_OneApp");
        file.HasOne<Tool>().WithMany().HasForeignKey(x => x.ToolId).OnDelete(DeleteBehavior.NoAction);
        file.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.NoAction);
        file.ConfigureAuditColumns("ToolFiles");
    }

    /// <summary>利用者設定とお気に入りの列および参照関係を構成します。</summary>
    private static void ConfigureUserModels(ModelBuilder model)
    {
        var preference = model.Entity<UserPreference>();
        preference.HasKey(x => x.UserId);
        preference.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserPreference>(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        preference.ConfigureAuditColumns("UserPreferences");

        var favorite = model.Entity<UserToolFavorite>();
        favorite.HasKey(x => new { x.UserId, x.ToolId });
        favorite.Property(x => x.ToolId).HasMaxLength(20).IsUnicode(false);
        favorite.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        favorite.HasOne<Tool>().WithMany().HasForeignKey(x => x.ToolId).OnDelete(DeleteBehavior.NoAction);
        favorite.ConfigureAuditColumns("UserToolFavorites");
    }

    /// <summary>お知らせ、FAQおよび問い合わせの列と参照関係を構成します。</summary>
    private static void ConfigureContentModels(ModelBuilder model)
    {
        var notice = model.Entity<Notice>();
        notice.HasKey(x => x.NoticeId);
        notice.Property(x => x.NoticeId).ValueGeneratedOnAdd();
        notice.Property(x => x.NoticeType).HasMaxLength(20).IsUnicode(false);
        notice.Property(x => x.ToolId).HasMaxLength(20).IsUnicode(false);
        notice.Property(x => x.Title).HasMaxLength(200).IsRequired();
        notice.Property(x => x.Content).HasColumnType("nvarchar(max)").IsRequired();
        notice.Property(x => x.MailSentAt).HasColumnType("datetime2(3)");
        notice.HasOne<Tool>().WithMany().HasForeignKey(x => x.ToolId).OnDelete(DeleteBehavior.NoAction);
        notice.ConfigureAuditColumns("Notices");

        var faqCategory = model.Entity<FaqCategory>();
        faqCategory.HasKey(x => x.CategoryId);
        faqCategory.Property(x => x.CategoryId).ValueGeneratedOnAdd();
        faqCategory.Property(x => x.CategoryName).HasMaxLength(100).IsRequired();
        faqCategory.ConfigureAuditColumns("FaqCategories");

        var faq = model.Entity<FaqItem>();
        faq.HasKey(x => x.FaqId);
        faq.Property(x => x.FaqId).ValueGeneratedOnAdd();
        faq.Property(x => x.Question).HasMaxLength(500).IsRequired();
        faq.Property(x => x.Answer).HasColumnType("nvarchar(max)").IsRequired();
        faq.HasIndex(x => new { x.CategoryId, x.SortOrder }).IsUnique().HasDatabaseName("UQ_FaqItems_Category_SortOrder");
        faq.HasOne<FaqCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
        faq.ConfigureAuditColumns("FaqItems");

        var inquiry = model.Entity<Inquiry>();
        inquiry.HasKey(x => x.InquiryId);
        inquiry.Property(x => x.InquiryId).HasColumnType("char(12)");
        inquiry.Property(x => x.CategoryCode).HasMaxLength(20).IsUnicode(false);
        inquiry.Property(x => x.TargetType).HasMaxLength(20).IsUnicode(false);
        inquiry.Property(x => x.ToolId).HasMaxLength(20).IsUnicode(false);
        inquiry.Property(x => x.Content).HasMaxLength(2000).IsRequired();
        inquiry.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
        inquiry.Property(x => x.AdminNote).HasMaxLength(2000);
        inquiry.HasOne<Tool>().WithMany().HasForeignKey(x => x.ToolId).OnDelete(DeleteBehavior.NoAction);
        inquiry.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.NoAction);
        inquiry.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssigneeUserId).OnDelete(DeleteBehavior.NoAction);
        inquiry.ConfigureAuditColumns("Inquiries");
    }
}
