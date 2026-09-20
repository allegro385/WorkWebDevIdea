using SalesSupport.Common.Configuration;

namespace SalesSupport.Common.DateTime;

/// <summary>業務処理用の日付を提供します。認証・監査には使用しません。</summary>
public interface IBusinessDateProvider
{
    /// <summary>指定業務日または現在のJST日付を返します。</summary>
    Task<DateOnly> GetTodayAsync(CancellationToken ct = default);
}

/// <summary>開発用日付と実時刻を分離します。</summary>
public sealed class BusinessDateProvider(ISystemSettingsReader settings, IApplicationClock clock) : IBusinessDateProvider
{
    /// <summary>設定のない場合だけ実際の日付を採用します。</summary>
    public async Task<DateOnly> GetTodayAsync(CancellationToken ct = default) => await settings.GetBusinessDateAsync(ct)
        ?? DateOnly.FromDateTime(clock.ToJst(clock.GetUtcNow()).DateTime);
}
