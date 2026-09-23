using SalesSupport.Common.DateTime;

namespace SalesSupport.Portal.Web.Services;

/// <summary>確認内容を持たない一回限りの操作を表す標識です。</summary>
/// <param name="Purpose">発行した画面・操作を識別する固定値です。</param>
public sealed record OneTimeTicket(string Purpose);

/// <summary>確認待ちデータを一度だけ実行させるためのサーバー側保持です。</summary>
/// <remarks>
/// 発行から30分、全用途合計で同時200件までメモリーに保持します。バイト数の独立上限は設けません。
/// プロセス再起動で失効し、永続化・自動再開は行いません。ブラウザーのhidden値を登録データの正本にしません。
/// </remarks>
public interface IConfirmationStore
{
    /// <summary>実行者へ束縛した確認データを保持し、確認IDを発行します。上限超過時はnullを返します。</summary>
    /// <param name="userId">確認と実行を行う本人のユーザーIDです。</param>
    /// <param name="payload">実行時に使用する確定済みのデータです。</param>
    Guid? Issue(Guid userId, object payload);

    /// <summary>確認IDを一度だけ使用し、本人・期限・種別が一致する場合だけデータを返します。</summary>
    /// <param name="confirmationId">画面から受け取った確認IDです。</param>
    /// <param name="userId">実行者本人のユーザーIDです。</param>
    /// <returns>使用できない場合はnullを返します。</returns>
    T? Consume<T>(Guid confirmationId, Guid userId) where T : class;
}

/// <summary>期限切れを取り除きながら、上限件数までの確認データを保持します。</summary>
public sealed class ConfirmationStore(IApplicationClock clock) : IConfirmationStore
{
    /// <summary>全用途で同時に保持する確認データの上限です。</summary>
    public const int Capacity = 200;

    /// <summary>確認データの発行からの保持期限です。</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly Dictionary<Guid, Entry> entries = new();
    private readonly object gate = new();

    /// <summary>期限切れを整理してから登録し、空きがない場合は発行しません。</summary>
    public Guid? Issue(Guid userId, object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var now = clock.GetUtcNow();
        lock (gate)
        {
            RemoveExpired(now);
            if (entries.Count >= Capacity) return null;
            var confirmationId = Guid.NewGuid();
            entries.Add(confirmationId, new Entry(userId, payload, now + Lifetime));
            return confirmationId;
        }
    }

    /// <summary>一致した確認データを取り出し、同じIDでの再実行を防ぎます。</summary>
    public T? Consume<T>(Guid confirmationId, Guid userId) where T : class
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            RemoveExpired(now);
            if (!entries.TryGetValue(confirmationId, out var entry)) return null;
            // 本人以外の要求では取り出さず、本人が期限内に使用できるよう対象データを維持します。
            if (entry.UserId != userId || entry.Payload is not T payload) return null;
            entries.Remove(confirmationId);
            return payload;
        }
    }

    /// <summary>期限を過ぎた確認データを保持量から除きます。</summary>
    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var expired in entries.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToList())
            entries.Remove(expired);
    }

    /// <summary>実行者と期限を伴う確認データ1件です。</summary>
    private sealed record Entry(Guid UserId, object Payload, DateTimeOffset ExpiresAt);
}
