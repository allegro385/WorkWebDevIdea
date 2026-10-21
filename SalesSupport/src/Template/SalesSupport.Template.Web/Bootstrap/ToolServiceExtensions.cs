using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DependencyInjection;
using SalesSupport.Template.Web.Services;

namespace SalesSupport.Template.Web.Bootstrap;

/// <summary>テンプレートツールのサービスを登録します。</summary>
public static class ToolServiceExtensions
{
    /// <summary>共通基盤とツール固有のサービスを登録します。認証・認可・MVC・CSRF・ログ・設定検証はCommonの登録に従います。</summary>
    /// <param name="services">ホストのサービスコレクションです。</param>
    /// <param name="environment">共通設定ファイルの基準になるコンテンツルートと、単独開発をCommonが検証するための実行環境です。</param>
    /// <param name="configuration">起動モード（起動プロファイル）と共通設定ファイルの場所を読むホスト設定です。</param>
    /// <returns>登録済みのサービスコレクションです。</returns>
    public static IServiceCollection AddSalesSupportTool(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        // 起動モードの確定、共通設定ファイルの読込み、ToolId・Portal URL・DB接続・鍵領域の検証、認証・認可・MVC・CSRF・ログの登録はCommonが行います。
        services.AddSalesSupportCommon(ApplicationKind.Tool, environment, configuration);
        // 一時保存領域を使うツールのため、未設定を起動時の構成エラーとして検出します。使用しないツールではこの検証を外します。
        // 単独開発はファイルを扱わず保存領域を持たないため、検証の対象外です。
        services.AddOptions<StorageOptions>()
            .Validate<IOptions<CommonOptions>>((x, common) => common.Value.IsStandalone || !string.IsNullOrWhiteSpace(x.TemporaryRoot), "Storage:TemporaryRootが必要です。").ValidateOnStart();
        services.AddScoped<IEstimateService, EstimateService>();
        return services;
    }
}
