using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Portal.Web.Controllers;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>ポータルトップが、認証モードに応じてお知らせ表示とツール一覧への寄せを切り替えることを確認します。</summary>
public sealed class HomeControllerTests
{
    /// <summary>匿名モードではトップを設けず、ツール一覧へ遷移させます。</summary>
    [Fact]
    public async Task AnonymousTopRedirectsToToolList()
    {
        var notices = new StubSystemNoticeService();
        var controller = Create(notices, anonymous: true);

        var result = await controller.Index(default);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Tools", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
        // お知らせは遷移先の一覧で取得するため、トップでは取得しません。
        Assert.Equal(0, notices.Calls);
    }

    /// <summary>ログインするモードでは、公開中のシステムお知らせを付けてトップを表示します。</summary>
    [Fact]
    public async Task LoginModeTopShowsSystemNotices()
    {
        var notices = new StubSystemNoticeService
        {
            Notices = [new(1, "メンテナンスのお知らせ", "本文", new DateOnly(2026, 10, 29), true)]
        };
        var controller = Create(notices);

        var result = await controller.Index(default);

        var model = Assert.IsType<HomeViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("メンテナンスのお知らせ", Assert.Single(model.Notices).Title);
    }

    /// <summary>HTTPコンテキスト付きの対象Controllerを生成します。</summary>
    /// <param name="notices">システムお知らせの取得に使用する代替実装です。</param>
    /// <param name="anonymous">匿名モードとして動かす場合はtrueです。</param>
    private static HomeController Create(ISystemNoticeService notices, bool anonymous = false) =>
        new(new StubSettingsReader(), notices, new StubCurrentUserAccessor(), new StubPreferenceService(), Options.Create(new CommonOptions
        {
            Kind = ApplicationKind.Portal,
            EnvironmentCode = "PRODUCTION",
            AuthenticationMode = anonymous ? SiteAuthenticationMode.Anonymous : SiteAuthenticationMode.EmailOtp
        }))
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

    /// <summary>検証済みの一般ユーザーを固定で返します。</summary>
    private sealed class StubCurrentUserAccessor : ICurrentUserAccessor
    {
        /// <summary>トップを表示できる一般ユーザーです。</summary>
        public CurrentUser? User { get; } = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "試験太郎", "A");
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
