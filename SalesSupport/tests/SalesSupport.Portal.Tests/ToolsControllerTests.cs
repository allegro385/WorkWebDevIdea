using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Contracts;
using SalesSupport.Portal.Web.Controllers;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>ツール一覧・詳細が、ログイン利用者と匿名モードの共通ユーザーでお気に入りの表示を切り替えることを確認します。</summary>
public sealed class ToolsControllerTests
{
    /// <summary>共通ユーザーの一覧は、お気に入りの操作とタブを表示しない指定にします。</summary>
    [Fact]
    public async Task CommonUserListHidesFavorites()
    {
        var controller = Create(CommonUser);

        var model = Assert.IsType<ToolListViewModel>(Assert.IsType<ViewResult>(await controller.Index(default)).Model);

        Assert.False(model.HasIdentifiedUser);
        Assert.False(model.FavoritesOnly);
    }

    /// <summary>ログインした利用者の一覧は、お気に入りの操作とタブを表示する指定にします。</summary>
    [Fact]
    public async Task SignedInListShowsFavorites()
    {
        var model = Assert.IsType<ToolListViewModel>(Assert.IsType<ViewResult>(await Create(SignedInUser).Index(default)).Model);

        Assert.True(model.HasIdentifiedUser);
    }

    /// <summary>共通ユーザーの詳細では、お気に入り一覧からの遷移として扱わず、開けない一覧への戻り先を作りません。</summary>
    [Fact]
    public async Task CommonUserDetailDoesNotReturnToFavoritesList()
    {
        var model = Assert.IsType<ToolDetailViewModel>(Assert.IsType<ViewResult>(await Create(CommonUser).Detail("T001", favorites: true, default)).Model);

        Assert.False(model.HasIdentifiedUser);
        Assert.False(model.FromFavorites);
    }

    /// <summary>ログインした利用者の詳細では、お気に入り一覧からの遷移をそのまま戻り先に使います。</summary>
    [Fact]
    public async Task SignedInDetailKeepsFavoritesReturn()
    {
        var model = Assert.IsType<ToolDetailViewModel>(Assert.IsType<ViewResult>(await Create(SignedInUser).Detail("T001", favorites: true, default)).Model);

        Assert.True(model.HasIdentifiedUser);
        Assert.True(model.FromFavorites);
    }

    /// <summary>匿名モードの固定共通ユーザーです。</summary>
    private static readonly CurrentUser CommonUser = new(AnonymousSiteUser.UserId, AnonymousSiteUser.DisplayName, AnonymousSiteUser.RoleCode);

    /// <summary>共有Cookieでログインした一般ユーザーです。</summary>
    private static readonly CurrentUser SignedInUser = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "試験太郎", "A");

    /// <summary>HTTPコンテキスト付きの対象Controllerを生成します。</summary>
    /// <param name="user">検証済み利用者です。</param>
    private static ToolsController Create(CurrentUser user) =>
        new(new StubToolQueryService(), new StubCurrentUserAccessor(user), new StubAccessEvaluator())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

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
            Task.FromResult<ToolDetailViewModel?>(new(toolId, "検証ツール", null, null, "WEB", "PUBLIC", "1.0.0",
                new DateOnly(2026, 10, 30), false, [], [], false, null, []));

        /// <summary>この試験では使用しません。</summary>
        public Task<string?> GetWebLaunchUrlAsync(string toolId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>指定した検証済み利用者を返します。</summary>
    /// <param name="user">一覧・詳細を表示する利用者です。</param>
    private sealed class StubCurrentUserAccessor(CurrentUser user) : ICurrentUserAccessor
    {
        /// <summary>一覧・詳細を表示する利用者です。</summary>
        public CurrentUser? User { get; } = user;
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
