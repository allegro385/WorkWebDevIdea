namespace SalesSupport.Common.DateTime;

/// <summary>業務日付の上書きから独立した実時刻を提供します。</summary>
public interface IApplicationClock
{
    /// <summary>実UTC時刻を返します。</summary>
    DateTimeOffset GetUtcNow();
    /// <summary>日本標準時へ変換します。</summary>
    DateTimeOffset ToJst(DateTimeOffset utc);
}

/// <summary>TimeProviderを使う実時刻取得です。</summary>
public sealed class ApplicationClock(TimeProvider timeProvider) : IApplicationClock
{
    /// <summary>現在のUTC時刻を返します。</summary>
    public DateTimeOffset GetUtcNow() => timeProvider.GetUtcNow();
    /// <summary>日本標準時へ変換します。</summary>
    public DateTimeOffset ToJst(DateTimeOffset utc) => utc.ToOffset(TimeSpan.FromHours(9));
}
