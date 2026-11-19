using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Portal.Web.Controllers;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>ポータルトップが、ログイン利用者と匿名モードの共通ユーザーの双方にお知らせを表示し、本人向けの入口を切り替えることを確認します。</summary>
public sealed class HomeControllerTests
{
    /// <summary>匿名モードの共通ユーザーにもトップを表示し、本人向けの入口は表示しない指定にします。</summary>
    [Fact]
    public async Task CommonUserTopShowsNoticesWithoutPersonalEntries()
    {
        var notices = new StubSystemNoticeService { Notices = [Notice()] };
        var controller = Create(notices, new(AnonymousSiteUser.UserId, AnonymousSiteUser.DisplayName, AnonymousSiteUser.RoleCode));

        var model = Assert.IsType<HomeViewModel>(Assert.IsType<ViewResult>(await controller.Index(default)).Model);

        Assert.Equal("メンテナンスのお知らせ", Assert.Single(model.Notices).Title);
        Assert.False(model.HasIdentifiedUser);
        Assert.False(model.CanUsePreferences);
        Assert.False(model.IsAdmin);
    }

    /// <summary>ログインした利用者には、公開中のシステムお知らせと本人向けの入口を表示します。</summary>
    [Fact]
    public async Task SignedInTopShowsSystemNotices()
    {
        var notices = new StubSystemNoticeService { Notices = [Notice()] };
        var controller = Create(notices, new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "試験太郎", "A"));

        var model = Assert.IsType<HomeViewModel>(Assert.IsType<ViewResult>(await controller.Index(default)).Model);

        Assert.Equal("メンテナンスのお知らせ", Assert.Single(model.Notices).Title);
        Assert.True(model.HasIdentifiedUser);
    }

    /// <summary>表示確認用のお知らせ1件を作成します。</summary>
    private static PortalNoticeView Notice() => new(1, "メンテナンスのお知らせ", "本文", new DateOnly(2026, 10, 29), true);

    /// <summary>HTTPコンテキスト付きの対象Controllerを生成します。</summary>
    /// <param name="notices">システムお知らせの取得に使用する代替実装です。</param>
    /// <param name="user">検証済み利用者です。</param>
    private static HomeController Create(ISystemNoticeService notices, CurrentUser user) =>
        new(new StubSettingsReader(), notices, new StubCurrentUserAccessor(user), new StubPreferenceService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    /// <summary>お知らせの取得結果と取得回数だけを差し替えます。</summary>
    private sealed class StubSystemNoticeService : ISystemNoticeService
    {
        /// <summary>取得結果として返すお知らせです。</summary>
        public IReadOnlyList<PortalNoticeView> Notices { get; set; } = [];

        /// <summary>取得が呼ばれた回数です。</summary>
        public int Calls { get; private set; }

        /// <summary>設定したお知らせをそのまま返します。</summary>
        /// <param name="ct">要求のキャンセルトークンです。</param>
        public Task<IReadOnlyList<PortalNoticeView>> GetPublishedAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Notices);
        }
    }

    /// <summary>この試験で参照しないサイト設定の取得を検出します。</summary>
    private sealed class StubSettingsReader : ISystemSettingsReader
    {
        /// <summary>サイト公開状態の取得を検出します。</summary>
        public Task<string> GetPublicationStatusAsync(CancellationToken ct = default) => throw new NotSupportedException();
        /// <summary>非公開案内の取得を検出します。</summary>
        public Task<string> GetPrivateMessageAsync(CancellationToken ct = default) => throw new NotSupportedException();
        /// <summary>業務日付の取得を検出します。</summary>
        public Task<DateOnly?> GetBusinessDateAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>指定した検証済み利用者を返します。</summary>
    /// <param name="user">トップを表示する利用者です。</param>
    private sealed class StubCurrentUserAccessor(CurrentUser user) : ICurrentUserAccessor
    {
        /// <summary>トップを表示する利用者です。</summary>
        public CurrentUser? User { get; } = user;
    }

    /// <summary>通知設定の利用を許可しない状態で固定します。</summary>
    private sealed class StubPreferenceService : IPreferenceService
    {
        /// <summary>個人設定のメニューを表示しない状態にします。</summary>
        public Task<bool> IsAllowedAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(false);
        /// <summary>この試験では使用しません。</summary>
        public Task<UserPreferencesDto?> GetAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        /// <summary>この試験では使用しません。</summary>
        public Task<PreferenceSaveResult> SaveAsync(Guid userId, UserPreferencesDto input, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
