using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Entities.Logging;

namespace SalesSupport.Common.Data;

/// <summary>業務トランザクションと独立してログを追記します。</summary>
public sealed class LogDbContext(DbContextOptions<LogDbContext> options) : DbContext(options)
{
    public DbSet<ToolUsageLog> ToolUsageLogs => Set<ToolUsageLog>();
    public DbSet<UserActivityLog> UserActivityLogs => Set<UserActivityLog>();
    public DbSet<SystemErrorLog> SystemErrorLogs => Set<SystemErrorLog>();

    /// <summary>既存ログDDLの型・長さに対応させます。</summary>
    protected override void OnModelCreating(ModelBuilder model)
    {
        var usage = model.Entity<ToolUsageLog>();
        usage.ToTable("ToolUsageLogs", "log");
        usage.Property(x => x.ToolId).HasMaxLength(20).IsUnicode(false);
        usage.Property(x => x.EventType).HasMaxLength(30).IsUnicode(false);
        usage.Property(x => x.ResultCode).HasMaxLength(20).IsUnicode(false);
        usage.Property(x => x.OccurredAt).HasColumnType("datetime2(3)");
        var activity = model.Entity<UserActivityLog>();
        activity.ToTable("UserActivityLogs", "log");
        activity.Property(x => x.EventType).HasMaxLength(50).IsUnicode(false);
        activity.Property(x => x.ResultCode).HasMaxLength(20).IsUnicode(false);
        activity.Property(x => x.FailureReason).HasMaxLength(50).IsUnicode(false);
        activity.Property(x => x.RequestPath).HasMaxLength(500);
        activity.Property(x => x.OperationTargetType).HasMaxLength(30).IsUnicode(false);
        activity.Property(x => x.OperationTargetId).HasMaxLength(100).IsUnicode(false);
        activity.Property(x => x.OperationDetails).HasMaxLength(1000);
        activity.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
        activity.Property(x => x.OccurredAt).HasColumnType("datetime2(3)");
        var error = model.Entity<SystemErrorLog>();
        error.ToTable("SystemErrorLogs", "log");
        error.HasIndex(x => x.ErrorId).IsUnique();
        error.Property(x => x.ErrorCode).HasMaxLength(100).IsUnicode(false);
        error.Property(x => x.ApplicationName).HasMaxLength(100).IsUnicode(false);
        error.Property(x => x.ToolId).HasMaxLength(20).IsUnicode(false);
        error.Property(x => x.ErrorLevel).HasMaxLength(20).IsUnicode(false);
        error.Property(x => x.ErrorType).HasMaxLength(300);
        error.Property(x => x.ErrorMessage).HasMaxLength(2000);
        error.Property(x => x.RequestPath).HasMaxLength(500);
        error.Property(x => x.HttpMethod).HasMaxLength(10).IsUnicode(false);
        error.Property(x => x.OccurredAt).HasColumnType("datetime2(3)");
    }
}
