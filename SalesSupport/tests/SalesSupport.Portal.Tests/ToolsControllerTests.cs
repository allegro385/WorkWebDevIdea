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

/// <summary>匿名モードのツール一覧が、画面上部へ公開中のシステムお知らせを渡すことを確認します。</summary>
public sealed class ToolsControllerTests
{
    /// <summary>匿名モードでは、取得したシステムお知らせと匿名モードの表示指定が表示情報へ入ります。</summary>
    [Fact]
    public async Task AnonymousListCarriesSystemNotices()
    {
        var notices = new StubSystemNoticeService { Notices = [Notice(1, "メンテナンスのお知らせ")] };
        var controller = Create(notices, anonymous: true);

        var result = await controller.Index(default);

        var model = Assert.IsType<ToolListViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.IsAnonymousMode);
        Assert.False(model.FavoritesOnly);
        Assert.Equal("メンテナンスのお知らせ", Assert.Single(model.Notices).Title);
    }

    /// <summary>公開中のお知らせが0件でも、匿名モードの一覧を表示情報付きで返します。</summary>
    [Fact]
    public async Task AnonymousListWithoutNoticesStillShowsList()
    {
        var controller = Create(new StubSystemNoticeService(), anonymous: true);

        var model = Assert.IsType<ToolListViewModel>(Assert.IsType<ViewResult>(await controller.Index(default)).Model);

        Assert.True(model.IsAnonymousMode);
        Assert.Empty(model.Notices);
    }

    /// <summary>ログインするモードの一覧はお知らせを取得せず、トップ画面だけに表示させます。</summary>
    [Fact]
    public async Task LoginModeListDoesNotReadNotices()
    {
        var notices = new StubSystemNoticeService { Notices = [Notice(1, "メンテナンスのお知らせ")] };
        var controller = Create(notices);

        var model = Assert.IsType<ToolListViewModel>(Assert.IsType<ViewResult>(await controller.Index(default)).Model);

        Assert.False(model.IsAnonymousMode);
        Assert.Empty(model.Notices);
        Assert.Equal(0, notices.Calls);
    }

    /// <summary>匿名モードの詳細では、お気に入り一覧からの遷移として扱わず、存在しない一覧への戻り先を作りません。</summary>
    [Fact]
    public async Task AnonymousDetailDoesNotReturnToFavoritesList()
    {
        var controller = Create(new StubSystemNoticeService(), anonymous: true);

        var result = await controller.Detail("T001", favorites: true, default);

        var model = Assert.IsType<ToolDetailViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.IsAnonymousMode);
        Assert.False(model.FromFavorites);
    }

    /// <summary>ログインするモードの詳細では、お気に入り一覧からの遷移をそのまま戻り先に使います。</summary>
    [Fact]
    public async Task LoginModeDetailKeepsFavoritesReturn()
    {
        var controller = Create(new StubSystemNoticeService());

        var model = Assert.IsType<ToolDetailViewModel>(Assert.IsType<ViewResult>(await controller.Detail("T001", favorites: true, default)).Model);

        Assert.True(model.FromFavorites);
    }

    /// <summary>表示確認用のお知らせ1件を作成します。</summary>
    /// <param name="noticeId">お知らせの識別子です。</param>
    /// <param name="title">お知らせの表題です。</param>
    private static PortalNoticeView Notice(int noticeId, string title) =>
        new(noticeId, title, "本文", new DateOnly(2026, 10, 29), true);

    /// <summary>HTTPコンテキスト付きの対象Controllerを生成します。</summary>
    /// <param name="notices">システムお知らせの取得に使用する代替実装です。</param>
    /// <param name="anonymous">匿名モードとして動かす場合はtrueです。</param>
    private static ToolsController Create(ISystemNoticeService notices, bool anonymous = false) =>
        new(new StubToolQueryService(), new StubCurrentUserAccessor(), new StubAccessEvaluator(), notices, Options.Create(new CommonOptions
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

    /// <summary>一覧の取得結果を空にし、タブの指定だけを返します。</summary>
    private sealed class StubToolQueryService : IToolQueryService
    {
        /// <summary>選択中のタブを反映した空の一覧を返します。</summary>
        /// <param name="userId">表示対象の利用者です。</param>
        /// <param name="roleCode">表示対象のロールです。</param>
        /// <param name="favoritesOnly">お気に入りツールタブを選択しているかどうかです。</param>
        /// <param name="ct">要求のキャンセルトークンです。</param>
        public Task<ToolListViewModel> GetListAsync(Guid userId, string roleCode, bool favoritesOnly, CancellationToken ct = default) =>
            Task.FromResult(new ToolListViewModel(favoritesOnly, 0, 0, []));

        /// <summary>表示できる最小限の詳細を返します。</summary>
        /// <param name="toolId">対象ツールです。</param>
        /// <param name="userId">表示対象の利用者です。</param>
        /// <param name="ct">要求のキャンセルトークンです。</param>
        public Task<ToolDetailViewModel?> GetDetailAsync(string toolId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<ToolDetailViewModel?>(new(toolId, "検証カテゴリ", "検証ツール", null, null, "WEB", "PUBLIC", "1.0.0",
                new DateOnly(2026, 10, 30), false, [], [], false, null, []));

        /// <summary>この試験では使用しません。</summary>
        public Task<string?> GetWebLaunchUrlAsync(string toolId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>検証済みの一般ユーザーを固定で返します。</summary>
    private sealed class StubCurrentUserAccessor : ICurrentUserAccessor
    {
        /// <summary>一覧を表示できる一般ユーザーです。</summary>
        public CurrentUser? User { get; } = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "試験太郎", "A");
    }

    /// <summary>利用制御を許可で固定します。</summary>
    private sealed class StubAccessEvaluator : IAccessEvaluator
    {
        /// <summary>常に許可を返します。</summary>
        /// <param name="request">認可対象の目的とツールです。</param>
        /// <param name="ct">要求のキャンセルトークンです。</param>
        public Task<AccessDecision> EvaluateAsync(AccessRequest request, CancellationToken ct = default) => Task.FromResult(new AccessDecision(true));
    }
}
