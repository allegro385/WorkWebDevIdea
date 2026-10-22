using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;

namespace SalesSupport.Common.Logging;

/// <summary>Webツールの利用ログを、設定のToolIdと固定の操作コードで記録します。</summary>
public interface IToolUsageLogger
{
    /// <summary>入口画面の利用（<c>WEB_OPEN</c>）を成功として一度記録します。</summary>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>ログ保存の成否です。本処理の成否とは独立しています。</returns>
    Task<LogWriteResult> WriteOpenAsync(CancellationToken ct = default);

    /// <summary>業務処理の実行（<c>WEB_EXECUTE</c>）を成功・失敗として一度記録します。</summary>
    /// <param name="succeeded">業務処理が成功した場合はtrueです。結果が未確定ならfalseを渡します。</param>
    /// <param name="ct">要求のキャンセルトークンです。中断済みの要求で失敗を記録する場合は<c>CancellationToken.None</c>を渡します。</param>
    /// <returns>ログ保存の成否です。本処理の成否とは独立しています。</returns>
    Task<LogWriteResult> WriteExecuteAsync(bool succeeded, CancellationToken ct = default);
}

/// <summary>ToolIdの解決と操作コードの組立てをまとめ、各ツールへ固定文字列を書き写させません。</summary>
public sealed class ToolUsageLogger(IUsageLogger usage, IOptions<CommonOptions> options) : IToolUsageLogger
{
    /// <summary>入口の利用を記録します。</summary>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>ログ保存の成否です。</returns>
    public Task<LogWriteResult> WriteOpenAsync(CancellationToken ct = default) => WriteAsync("WEB_OPEN", "SUCCESS", ct);

    /// <summary>実行結果を記録します。</summary>
    /// <param name="succeeded">業務処理が成功した場合はtrueです。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>ログ保存の成否です。</returns>
    public Task<LogWriteResult> WriteExecuteAsync(bool succeeded, CancellationToken ct = default) =>
        WriteAsync("WEB_EXECUTE", succeeded ? "SUCCESS" : "FAILURE", ct);

    /// <summary>設定のToolIdを添えて利用ログへ渡します。ToolId未設定は空文字とし、採否はCommonLoggerが判断します。</summary>
    /// <param name="eventType">記録する操作種別です。</param>
    /// <param name="resultCode">記録する結果です。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    /// <returns>ログ保存の成否です。</returns>
    private Task<LogWriteResult> WriteAsync(string eventType, string resultCode, CancellationToken ct) =>
        usage.WriteAsync(new UsageEvent(options.Value.ToolId ?? "", eventType, resultCode), ct);
}
