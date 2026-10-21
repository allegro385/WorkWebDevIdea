using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DependencyInjection;
using SalesSupport.SampleEstimate.Web.Data;
using SalesSupport.SampleEstimate.Web.Services;

namespace SalesSupport.SampleEstimate.Web.Bootstrap;

/// <summary>見積試算サンプルの共通基盤と専用DBサービスを登録します。</summary>
public static class ToolServiceExtensions
{
    /// <summary>共通基盤とツール固有のサービスを登録します。認証・認可・ログ・設定検証はCommonの登録に従います。</summary>
    /// <param name="services">ホストのサービスコレクションです。</param>
    /// <param name="environment">共通設定ファイルの基準になるコンテンツルートと、単独開発をCommonが検証するための実行環境です。</param>
    /// <param name="configuration">起動モード（起動プロファイル）と共通設定ファイルの場所を読むホスト設定です。</param>
    /// <returns>登録済みのサービスコレクションです。</returns>
    public static IServiceCollection AddSalesSupportTool(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        // 起動モードの確定、共通設定ファイルの読込み、ToolId・Portal URL・DB接続・鍵領域の検証、認証・認可・ログの登録はCommonが行います。
        services.AddSalesSupportCommon(ApplicationKind.Tool, environment, configuration);
        // サンプルの表だけをこのDbContextで管理し、接続先はCommonと同じ設定から取得します。
        services.AddDbContext<SampleEstimateDbContext>((provider, options) =>
        {
            // 単独開発は接続文字列を持たないため、DBへ接続しない設定で登録します。
            if (provider.GetRequiredService<IOptions<CommonOptions>>().Value.IsStandalone) options.UseSqlServer();
            else options.UseSqlServer(provider.GetRequiredService<IConnectionStringProvider>().SalesSupportDatabase);
        });
        services.AddScoped<IEstimateRecordService, EstimateRecordService>();
        services.AddScoped<IToolStatusService, ToolStatusService>();
        // 一時保存領域を使うツールのため、未設定を起動時の構成エラーとして検出します。使用しないツールではこの検証を外します。
        // 単独開発はファイルを扱わず保存領域を持たないため、検証の対象外です。
        services.AddOptions<StorageOptions>()
            .Validate<IOptions<CommonOptions>>((x, common) => common.Value.IsStandalone || !string.IsNullOrWhiteSpace(x.TemporaryRoot), "Storage:TemporaryRootが必要です。").ValidateOnStart();
        services.AddScoped<IEstimateService, EstimateService>();
        // 更新要求のCSRF検証を既定で適用します。個別のControllerで無効化しません。
        services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
        return services;
    }
}
