using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Portal.Web.Controllers;

namespace SalesSupport.Portal.Web.Bootstrap;

/// <summary>匿名モードのときだけ、ログインが前提の画面を外す規約をMVCへ追加します。</summary>
public sealed class AnonymousModeMvcSetup(IOptions<CommonOptions> common) : IConfigureOptions<MvcOptions>
{
    /// <summary>認証モードを確認し、匿名モードの場合だけ除外の規約を追加します。</summary>
    /// <param name="options">規約を追加するMVCの設定です。</param>
    public void Configure(MvcOptions options)
    {
        if (common.Value.IsAnonymous) options.Conventions.Add(new AnonymousModeConvention());
    }
}

/// <summary>ログインした本人を前提とする画面と通知設定APIを、経路ごと存在しない状態にします。</summary>
/// <remarks>表示の抑止ではなく要求処理からの除外であり、直接URLを入力しても404になります。認可の判定はCommonが引き続き行います。</remarks>
public sealed class AnonymousModeConvention : IApplicationModelConvention
{
    /// <summary>匿名モードで提供しないControllerです。</summary>
    private static readonly HashSet<Type> Removed =
    [
        typeof(AccountController), typeof(FavoritesController), typeof(PreferencesController), typeof(InquiriesController),
        typeof(Areas.Admin.Controllers.UsersController), typeof(Areas.Admin.Controllers.ToolsController),
        typeof(Areas.Admin.Controllers.NoticesController), typeof(Areas.Admin.Controllers.InquiriesController)
    ];

    /// <summary>Controllerごと外せないため、アクション単位で除外する対象です。</summary>
    /// <remarks>ツールのControllerはツール一覧・詳細・Web起動を提供し続けるため、お気に入り一覧だけを外します。
    /// 通知設定APIも同じControllerにある現在ユーザーのアクションを各ツールが使うため残します。</remarks>
    private static readonly (Type Controller, string[] Actions)[] RemovedActions =
    [
        (typeof(ToolsController), [nameof(ToolsController.Favorites)]),
        (typeof(UsersApiController), [nameof(UsersApiController.GetPreferences), nameof(UsersApiController.PutPreferences)])
    ];

    /// <summary>対象のControllerを除外し、お気に入り一覧と通知設定APIはアクション単位で除外します。</summary>
    /// <param name="application">要求処理へ登録するControllerとアクションの一覧です。</param>
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers.Where(x => Removed.Contains(x.ControllerType.AsType())).ToList())
        {
            application.Controllers.Remove(controller);
        }
        foreach (var (type, actions) in RemovedActions)
        {
            var controller = application.Controllers.SingleOrDefault(x => x.ControllerType.AsType() == type);
            if (controller is null) continue;
            foreach (var action in controller.Actions.Where(x => actions.Contains(x.ActionMethod.Name)).ToList())
            {
                controller.Actions.Remove(action);
            }
        }
    }
}
