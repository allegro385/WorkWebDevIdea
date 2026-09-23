using SalesSupport.Common.DateTime;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>確認IDが本人束縛・一回消費・期限付きで動作することを確認します。</summary>
public sealed class ConfirmationStoreTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>発行した確認IDを本人が一度だけ使用できることを確認します。</summary>
    [Fact]
    public void ConsumeReturnsPayloadOnlyOnce()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-09-21T00:00:00Z"));
        var store = new ConfirmationStore(clock);
        var confirmationId = store.Issue(Owner, new OneTimeTicket("TEST"));

        Assert.NotNull(confirmationId);
        Assert.Equal("TEST", store.Consume<OneTimeTicket>(confirmationId!.Value, Owner)?.Purpose);
        Assert.Null(store.Consume<OneTimeTicket>(confirmationId.Value, Owner));
    }

    /// <summary>実行者が異なる場合は取り出せず、本人のために確認内容が残ることを確認します。</summary>
    [Fact]
    public void ConsumeRejectsOtherUser()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-09-21T00:00:00Z"));
        var store = new ConfirmationStore(clock);
        var confirmationId = store.Issue(Owner, new OneTimeTicket("TEST"))!.Value;

        Assert.Null(store.Consume<OneTimeTicket>(confirmationId, Other));
        Assert.NotNull(store.Consume<OneTimeTicket>(confirmationId, Owner));
    }

    /// <summary>種別が異なる確認内容を取り出さないことを確認します。</summary>
    [Fact]
    public void ConsumeRejectsOtherPayloadType()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-09-21T00:00:00Z"));
        var store = new ConfirmationStore(clock);
        var confirmationId = store.Issue(Owner, new OneTimeTicket("TEST"))!.Value;

        Assert.Null(store.Consume<string>(confirmationId, Owner));
    }

    /// <summary>保持期限を過ぎた確認IDを使用できないことを確認します。</summary>
    [Fact]
    public void ConsumeRejectsExpiredConfirmation()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-09-21T00:00:00Z"));
        var store = new ConfirmationStore(clock);
        var confirmationId = store.Issue(Owner, new OneTimeTicket("TEST"))!.Value;

        clock.Advance(ConfirmationStore.Lifetime + TimeSpan.FromSeconds(1));
        Assert.Null(store.Consume<OneTimeTicket>(confirmationId, Owner));
    }

    /// <summary>上限に達した場合は発行せず、期限切れの整理後は発行できることを確認します。</summary>
    [Fact]
    public void IssueStopsAtCapacityAndResumesAfterExpiry()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-09-21T00:00:00Z"));
        var store = new ConfirmationStore(clock);
        for (var index = 0; index < ConfirmationStore.Capacity; index++) Assert.NotNull(store.Issue(Owner, new OneTimeTicket("TEST")));

        Assert.Null(store.Issue(Owner, new OneTimeTicket("TEST")));
        clock.Advance(ConfirmationStore.Lifetime + TimeSpan.FromSeconds(1));
        Assert.NotNull(store.Issue(Owner, new OneTimeTicket("TEST")));
    }

    /// <summary>テストから時刻を進められる実時刻の差し替えです。</summary>
    private sealed class FixedClock(DateTimeOffset start) : IApplicationClock
    {
        private DateTimeOffset now = start;

        /// <summary>設定した現在時刻を返します。</summary>
        public DateTimeOffset GetUtcNow() => now;

        /// <summary>日本標準時へ変換します。</summary>
        public DateTimeOffset ToJst(DateTimeOffset utc) => utc.ToOffset(TimeSpan.FromHours(9));

        /// <summary>現在時刻を指定時間だけ進めます。</summary>
        public void Advance(TimeSpan span) => now += span;
    }
}
