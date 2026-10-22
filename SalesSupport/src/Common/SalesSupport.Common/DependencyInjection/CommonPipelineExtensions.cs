using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.ErrorHandling;

namespace SalesSupport.Common.DependencyInjection;

/// <summary>共通処理をホストのミドルウェアパイプラインへ組み込みます。順序の定義はこのクラスだけが持ちます。</summary>
public static class CommonPipelineExtensions
{
    /// <summary>全応答へ付与するセキュリティヘッダーです。値の根拠は[Common詳細設計]のセキュリティヘッダーに従います。</summary>
    private static readonly (string Name, string Value)[] SecurityHeaders =
    [
        ("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'"),
        ("X-Content-Type-Options", "nosniff"),
        ("X-Frame-Options", "DENY"),
        ("Referrer-Policy", "same-origin")
    ];

    /// <summary>Portalの要求パイプラインを組み込みます。認証の後、認可の前に接続元単位の制限を適用します。</summary>
    /// <param name="app">組込み先のホストです。</param>
    /// <returns>組込み済みのホストです。</returns>
    public static WebApplication UseSalesSupportPortal(this WebApplication app) => UseSalesSupportPipeline(app, "Home", useRateLimiter: true);

    /// <summary>Webツールの要求パイプラインを組み込みます。ツール側へ個別のミドルウェア呼出しを展開しません。</summary>
    /// <param name="app">組込み先のホストです。</param>
    /// <returns>組込み済みのホストです。</returns>
    public static WebApplication UseSalesSupportTool(this WebApplication app) => UseSalesSupportPipeline(app, "Tool", useRateLimiter: false);

    /// <summary>応答へ共通のセキュリティヘッダーを付与します。</summary>
    /// <param name="app">組込み先のホストです。</param>
    /// <returns>組込み済みのホストです。</returns>
    public static IApplicationBuilder UseSalesSupportSecurityHeaders(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        // 共通例外処理はResponse.Clear()で応答を作り直すため、送信直前に付与してヘッダーが消えないようにします。
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            foreach (var (name, value) in SecurityHeaders) headers[name] = value;
            return Task.CompletedTask;
        }, context);
        return next(context);
    });

    /// <summary>設定したプロキシのみを信頼します。未設定では転送ヘッダーを採用しません。</summary>
    /// <param name="app">組込み先のホストです。</param>
    /// <returns>組込み済みのホストです。</returns>
    internal static IApplicationBuilder UseSalesSupportForwardedHeaders(this IApplicationBuilder app)
    {
        var proxies = app.ApplicationServices.GetRequiredService<IOptions<ProxyOptions>>().Value.KnownProxies;
        if (proxies.Length == 0) return app;
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var address in proxies) options.KnownProxies.Add(System.Net.IPAddress.Parse(address));
        return app.UseForwardedHeaders(options);
    }

    /// <summary>PortalとWebツールで共通の順序を組み込みます。差異は既定のコントローラーと接続元単位の制限だけです。</summary>
    /// <param name="app">組込み先のホストです。</param>
    /// <param name="defaultController">既定のルートで使うコントローラー名です。</param>
    /// <param name="useRateLimiter">接続元単位の制限を適用する場合はtrueです。登録はホストが行います。</param>
    /// <returns>組込み済みのホストです。</returns>
    private static WebApplication UseSalesSupportPipeline(WebApplication app, string defaultController, bool useRateLimiter)
    {
        app.UseSalesSupportForwardedHeaders();
        app.UseSalesSupportSecurityHeaders();
        app.UseSalesSupportErrors();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        // 既定の認可方針は保護対象の画面にだけ適用し、共通レイアウトが読み込む資産は匿名で配信します。
        app.MapStaticAssets().AllowAnonymous();
        // EndpointメタデータをCookie更新判定で参照できるよう、ルーティングを認証より前に配置します。
        app.UseRouting();
        app.UseAuthentication();
        // 接続元単位の制限は認証の後、認可の前に適用し、超過分を待機させません。
        if (useRateLimiter) app.UseRateLimiter();
        app.UseAuthorization();

        // ヘッダーのログアウトはCommonの共通処理で共有Cookieを破棄し、Portalへ戻します。
        app.MapSalesSupportLogout();

        app.MapControllerRoute(
            name: "default",
            pattern: $"{{controller={defaultController}}}/{{action=Index}}/{{id?}}")
            .WithStaticAssets();

        return app;
    }
}
