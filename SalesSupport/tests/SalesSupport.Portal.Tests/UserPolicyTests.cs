using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using SalesSupport.Common.Authentication;
using SalesSupport.Portal.Web.Controllers;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>本人を要求する画面・APIと管理画面が、匿名モードの共通ユーザーでは満たせない明示ポリシーを持つことを確認します。</summary>
public sealed class UserPolicyTests
{
    /// <summary>お気に入り・個人設定・問い合わせはController全体で本人のポリシーを要求します。</summary>
    /// <param name="controller">確認するControllerです。</param>
    [Theory]
    [InlineData(typeof(FavoritesController))]
    [InlineData(typeof(PreferencesController))]
    [InlineData(typeof(InquiriesController))]
    public void PersonalControllersRequireUser(Type controller) => Assert.Equal(CommonPolicies.User, Policy(controller));

    /// <summary>一覧・詳細・現在ユーザーAPIと同じControllerにある本人向けアクションだけに、本人のポリシーを付けます。</summary>
    /// <param name="controller">確認するControllerです。</param>
    /// <param name="action">確認するアクションです。</param>
    [Theory]
    [InlineData(typeof(ToolsController), nameof(ToolsController.Favorites))]
    [InlineData(typeof(UsersApiController), nameof(UsersApiController.GetPreferences))]
    [InlineData(typeof(UsersApiController), nameof(UsersApiController.PutPreferences))]
    public void PersonalActionsRequireUser(Type controller, string action) => Assert.Equal(CommonPolicies.User, Policy(controller.GetMethod(action)!));

    /// <summary>共通ユーザーも使う一覧・詳細・起動・現在ユーザーAPIには明示ポリシーを付けず、既定方針で判定します。</summary>
    /// <param name="controller">確認するControllerです。</param>
    /// <param name="action">確認するアクションです。</param>
    [Theory]
    [InlineData(typeof(ToolsController), nameof(ToolsController.Index))]
    [InlineData(typeof(ToolsController), nameof(ToolsController.Detail))]
    [InlineData(typeof(ToolsController), nameof(ToolsController.Launch))]
    [InlineData(typeof(UsersApiController), nameof(UsersApiController.Me))]
    public void SharedActionsUseFallback(Type controller, string action)
    {
        Assert.Null(Policy(controller));
        Assert.Null(Policy(controller.GetMethod(action)!));
    }

    /// <summary>管理画面は全Controllerで管理者のポリシーを要求します。</summary>
    /// <param name="controller">確認するControllerです。</param>
    [Theory]
    [InlineData(typeof(SalesSupport.Portal.Web.Areas.Admin.Controllers.UsersController))]
    [InlineData(typeof(SalesSupport.Portal.Web.Areas.Admin.Controllers.ToolsController))]
    [InlineData(typeof(SalesSupport.Portal.Web.Areas.Admin.Controllers.NoticesController))]
    [InlineData(typeof(SalesSupport.Portal.Web.Areas.Admin.Controllers.InquiriesController))]
    public void AdminControllersRequireAdmin(Type controller) => Assert.Equal(CommonPolicies.Admin, Policy(controller));

    /// <summary>直接付与されたAuthorize属性のポリシー名を返します。</summary>
    /// <param name="member">ControllerまたはActionです。</param>
    /// <returns>ポリシー名です。属性がなければnullです。</returns>
    private static string? Policy(MemberInfo member) => member.GetCustomAttribute<AuthorizeAttribute>(inherit: false)?.Policy;
}
