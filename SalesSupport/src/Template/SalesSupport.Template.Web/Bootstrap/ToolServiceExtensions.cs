using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DependencyInjection;
using SalesSupport.Template.Web.Services;

namespace SalesSupport.Template.Web.Bootstrap;

/// <summary>テンプレートツールのサービスを登録します。</summary>
public static class ToolServiceExtensions
{
    /// <summary>共通基盤とツール固有のサービスを登録します。認証・認可・ログ・設定検証はCommonの登録に従います。</summary>
    /// <param name="services">ホストのサービスコレクションです。</param>
    /// <returns>登録済みのサービスコレクションです。</returns>
    public static IServiceCollection AddSalesSupportTool(this IServiceCollection services)
    {
        // 共通設定ファイルの読込み、ToolId・Portal URL・DB接続・鍵領域の検証、認証・認可・ログの登録はCommonが行います。
        services.AddSalesSupportCommon(ApplicationKind.Tool);
        // 一時保存領域を使うツールのため、未設定を起動時の構成エラーとして検出します。使用しないツールではこの検証を外します。
        services.AddOptions<StorageOptions>()
            .Validate(x => !string.IsNullOrWhiteSpace(x.TemporaryRoot), "Storage:TemporaryRootが必要です。").ValidateOnStart();
        services.AddScoped<IEstimateService, EstimateService>();
        // 更新要求のCSRF検証を既定で適用します。個別のControllerで無効化しません。
        services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
        return services;
    }
}
