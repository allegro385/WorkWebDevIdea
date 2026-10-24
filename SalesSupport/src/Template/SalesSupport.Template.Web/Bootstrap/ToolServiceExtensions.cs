using SalesSupport.Common.Contracts;
using SalesSupport.Common.DependencyInjection;
using SalesSupport.Template.Web.TemplateExample;

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
        // 一時保存領域を使うツールのため、未設定を起動時の構成エラーとして検出します。使用しないツールではこの呼出しを外します。
        services.RequireTemporaryStorage();
        services.AddScoped<IExampleService, ExampleService>();
        return services;
    }
}
