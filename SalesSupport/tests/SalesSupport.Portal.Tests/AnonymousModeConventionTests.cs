using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Portal.Web.Bootstrap;
using SalesSupport.Portal.Web.Controllers;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>匿名モードで、ログインした本人を前提とする画面と通知設定APIが要求処理から外れることを確認します。</summary>
public sealed class AnonymousModeConventionTests
{
    /// <summary>匿名モードのときだけ除外の規約を追加します。</summary>
    /// <param name="anonymous">匿名モードとして動かす場合はtrueです。</param>
    /// <param name="expected">規約を追加するかどうかです。</param>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ConventionIsAddedOnlyInAnonymousMode(bool anonymous, bool expected)
    {
        var options = new MvcOptions();

        new AnonymousModeMvcSetup(Options.Create(new CommonOptions
        {
            Kind = ApplicationKind.Portal,
            EnvironmentCode = "PRODUCTION",
            AuthenticationMode = anonymous ? SiteAuthenticationMode.Anonymous : SiteAuthenticationMode.EmailOtp
        })).Configure(options);

        Assert.Equal(expected, options.Conventions.Any(x => x is AnonymousModeConvention));
    }

    /// <summary>ログイン・お気に入り・個人設定・問い合わせと管理画面を除外し、一覧・詳細・マニュアル・ファイル取得を残します。</summary>
    [Fact]
    public void LoginOnlyControllersAreRemoved()
    {
        var application = Application();

        new AnonymousModeConvention().Apply(application);

        var remaining = application.Controllers.Select(x => x.ControllerType.AsType()).ToList();
        Assert.DoesNotContain(typeof(AccountController), remaining);
        Assert.DoesNotContain(typeof(FavoritesController), remaining);
        Assert.DoesNotContain(typeof(PreferencesController), remaining);
        Assert.DoesNotContain(typeof(InquiriesController), remaining);
        Assert.DoesNotContain(typeof(Web.Areas.Admin.Controllers.UsersController), remaining);
        Assert.DoesNotContain(typeof(Web.Areas.Admin.Controllers.ToolsController), remaining);
        Assert.DoesNotContain(typeof(Web.Areas.Admin.Controllers.NoticesController), remaining);
        Assert.DoesNotContain(typeof(Web.Areas.Admin.Controllers.InquiriesController), remaining);
        Assert.Contains(typeof(HomeController), remaining);
        Assert.Contains(typeof(ToolsController), remaining);
        Assert.Contains(typeof(HelpController), remaining);
        Assert.Contains(typeof(ToolFilesController), remaining);
        Assert.Contains(typeof(UsersApiController), remaining);
    }

    /// <summary>通知設定APIだけを外し、各ツールが使う現在ユーザーのAPIは残します。</summary>
    [Fact]
    public void OnlyPreferenceApiActionsAreRemoved()
    {
        var application = Application();

        new AnonymousModeConvention().Apply(application);

        var api = Assert.Single(application.Controllers, x => x.ControllerType.AsType() == typeof(UsersApiController));
        Assert.Equal([nameof(UsersApiController.Me)], api.Actions.Select(x => x.ActionMethod.Name));
    }

    /// <summary>Portalの各Controllerと、通知設定APIの3アクションを持つ検証用のモデルを作成します。</summary>
    /// <returns>規約の適用対象となる検証用のアプリケーションモデルです。</returns>
    private static ApplicationModel Application()
    {
        var application = new ApplicationModel();
        foreach (var type in new[]
        {
            typeof(HomeController), typeof(ToolsController), typeof(HelpController), typeof(ToolFilesController),
            typeof(AccountController), typeof(FavoritesController), typeof(PreferencesController), typeof(InquiriesController),
            typeof(Web.Areas.Admin.Controllers.UsersController), typeof(Web.Areas.Admin.Controllers.ToolsController),
            typeof(Web.Areas.Admin.Controllers.NoticesController), typeof(Web.Areas.Admin.Controllers.InquiriesController)
        })
        {
            application.Controllers.Add(new ControllerModel(type.GetTypeInfo(), []));
        }
        var api = new ControllerModel(typeof(UsersApiController).GetTypeInfo(), []);
        foreach (var name in new[] { nameof(UsersApiController.Me), nameof(UsersApiController.GetPreferences), nameof(UsersApiController.PutPreferences) })
        {
            api.Actions.Add(new ActionModel(typeof(UsersApiController).GetMethod(name)!, []));
        }
        application.Controllers.Add(api);
        return application;
    }
}
