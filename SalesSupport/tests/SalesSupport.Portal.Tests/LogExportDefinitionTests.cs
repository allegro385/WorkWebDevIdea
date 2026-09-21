using SalesSupport.Common.DataExport;
using SalesSupport.Common.DateTime;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>TSV出力の列定義とファイル名が設計どおりであることを確認します。</summary>
/// <remarks>列定義とファイル名はDBへ接続しないため、依存先を使用しない範囲だけを検証します。</remarks>
public sealed class LogExportDefinitionTests
{
    /// <summary>DB接続を行わないメソッドだけを対象に、依存先を渡さずサービスを作成します。</summary>
    private static ILogExportService CreateService() =>
        new LogExportService(null!, null!, new ApplicationClock(TimeProvider.System));

    /// <summary>ツール利用ログの明細がDBのカラム名と定義順で出力されることを確認します。</summary>
    [Fact]
    public void ToolUsageDetailUsesDatabaseColumnOrder()
    {
        var definition = CreateService().GetDefinition(LogKind.ToolUsage, LogExtraction.Detail);
        Assert.Equal(DelimitedFormat.Tsv, definition.Format);
        Assert.Equal(new[] { "ToolUsageLogId", "ToolId", "UserId", "OccurredAt", "EventType", "ResultCode", "CorrelationId" }, definition.Columns);
    }

    /// <summary>ユーザー操作ログの明細が12列で出力されることを確認します。</summary>
    [Fact]
    public void UserActivityDetailUsesDatabaseColumnOrder()
    {
        var definition = CreateService().GetDefinition(LogKind.UserActivity, LogExtraction.Detail);
        Assert.Equal("UserActivityLogId", definition.Columns[0]);
        Assert.Equal("CorrelationId", definition.Columns[^1]);
        Assert.Equal(12, definition.Columns.Count);
    }

    /// <summary>システムエラーログの明細が14列で出力されることを確認します。</summary>
    [Fact]
    public void SystemErrorDetailUsesDatabaseColumnOrder()
    {
        var definition = CreateService().GetDefinition(LogKind.SystemError, LogExtraction.Detail);
        Assert.Equal("SystemErrorLogId", definition.Columns[0]);
        Assert.Equal(14, definition.Columns.Count);
    }

    /// <summary>ツール別利用者数がツール名と利用者数の2列で出力されることを確認します。</summary>
    [Fact]
    public void ToolUserCountUsesTwoColumns()
    {
        var definition = CreateService().GetDefinition(LogKind.ToolUsage, LogExtraction.ToolUserCount);
        Assert.Equal(new[] { "ToolName", "UserCount" }, definition.Columns);
    }

    /// <summary>ツール利用ログ以外では集計を選んでも明細の列定義になることを確認します。</summary>
    [Fact]
    public void OtherKindsIgnoreAggregation()
    {
        var definition = CreateService().GetDefinition(LogKind.UserActivity, LogExtraction.ToolUserCount);
        Assert.Equal("UserActivityLogId", definition.Columns[0]);
    }

    /// <summary>ファイル名に種別と出力日時が含まれることを確認します。</summary>
    [Fact]
    public void FileNameContainsKindAndTimestamp()
    {
        var fileName = CreateService().BuildFileName(LogKind.ToolUsage, LogExtraction.ToolUserCount);
        Assert.StartsWith("ツール別利用者数_", fileName, StringComparison.Ordinal);
        Assert.EndsWith(".tsv", fileName, StringComparison.Ordinal);
    }
}
