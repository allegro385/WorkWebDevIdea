using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Data;
using SalesSupport.Common.Entities.Logging;
using SalesSupport.Common.Logging;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>利用ログの備考を、ログ行を失わずに保存できる形へ整えることを確認します。</summary>
public sealed class UsageLogNoteTests
{
    /// <summary>制御文字と前後の空白を除き、内容がなければNULLとして保存します。</summary>
    /// <param name="note">ツールが指定した備考です。</param>
    /// <param name="expected">保存される備考です。</param>
    [Theory]
    [InlineData("備考", "備考")]
    [InlineData("  備考　", "備考")]
    [InlineData("備\r\n考\t", "備考")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public async Task NormalizesNote(string? note, string? expected)
    {
        var saved = await SaveAsync(note);

        Assert.Equal(expected, saved.Note);
    }

    /// <summary>上限を超える備考は切り詰め、DBの長さ超過でログ行全体を失わないようにします。</summary>
    [Fact]
    public async Task TruncatesNoteToMaxLength()
    {
        var saved = await SaveAsync(new string('あ', ToolUsageLog.NoteMaxLength + 1));

        Assert.Equal(new string('あ', ToolUsageLog.NoteMaxLength), saved.Note);
    }

    /// <summary>DB定義と同じ長さをEF Coreのマッピングへ指定します。</summary>
    [Fact]
    public void MapsNoteToUnicodeColumnWithMaxLength()
    {
        using var db = new LogDbContext(new DbContextOptionsBuilder<LogDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var property = db.Model.FindEntityType(typeof(ToolUsageLog))!.FindProperty(nameof(ToolUsageLog.Note))!;

        Assert.Equal(500, ToolUsageLog.NoteMaxLength);
        Assert.Equal(ToolUsageLog.NoteMaxLength, property.GetMaxLength());
        Assert.True(property.IsNullable);
    }

    /// <summary>備考を指定した実行の利用ログを保存し、保存された行を返します。</summary>
    /// <param name="note">ツールが指定した備考です。</param>
    /// <returns>メモリ上DBへ保存された行です。</returns>
    private static async Task<ToolUsageLog> SaveAsync(string? note)
    {
        var factory = new MemoryLogFactory(new DbContextOptionsBuilder<LogDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(Guid.NewGuid(), "利用者", "A"));
        var logger = new CommonLogger(factory, current, new SalesSupport.Common.DateTime.ApplicationClock(TimeProvider.System), new RequestCorrelation(),
            Options.Create(new CommonOptions { Kind = ApplicationKind.Tool, ToolId = "T001" }), Options.Create(new LoggingOptions()), new HttpContextAccessor());

        Assert.Equal(LogWriteResult.Written, await logger.WriteAsync(new UsageEvent("T001", "WEB_EXECUTE", "SUCCESS", note)));

        await using var db = factory.CreateDbContext();
        return await db.ToolUsageLogs.SingleAsync();
    }

    /// <summary>SQLへ接続しないログ用Contextを供給します。</summary>
    /// <param name="options">同じメモリ上DBを指す設定です。</param>
    private sealed class MemoryLogFactory(DbContextOptions<LogDbContext> options) : IDbContextFactory<LogDbContext>
    {
        /// <summary>専用のメモリ上DBへ書き込むContextを作成します。</summary>
        /// <returns>新しいContextです。</returns>
        public LogDbContext CreateDbContext() => new(options);
    }
}
