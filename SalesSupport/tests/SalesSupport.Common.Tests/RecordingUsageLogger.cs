using SalesSupport.Common.Logging;

namespace SalesSupport.Common.Tests;

/// <summary>記録された利用ログを保持する検証用の記録先です。DBへは書き込みません。</summary>
internal sealed class RecordingUsageLogger : IUsageLogger
{
    /// <summary>記録順の利用ログです。</summary>
    public List<UsageEvent> Events { get; } = [];

    /// <summary>記録内容をテストから確認できるように保持します。</summary>
    public Task<LogWriteResult> WriteAsync(UsageEvent entry, CancellationToken ct = default)
    {
        Events.Add(entry);
        return Task.FromResult(LogWriteResult.Written);
    }
}
