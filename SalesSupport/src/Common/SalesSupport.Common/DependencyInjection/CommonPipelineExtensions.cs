using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;

namespace SalesSupport.Common.DependencyInjection;

/// <summary>共通処理をホストのミドルウェアパイプラインへ組み込みます。</summary>
public static class CommonPipelineExtensions
{
    /// <summary>設定したプロキシのみを信頼します。未設定では転送ヘッダーを採用しません。</summary>
    public static IApplicationBuilder UseSalesSupportForwardedHeaders(this IApplicationBuilder app)
    {
        var proxies = app.ApplicationServices.GetRequiredService<IOptions<ProxyOptions>>().Value.KnownProxies;
        if (proxies.Length == 0) return app;
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var address in proxies) options.KnownProxies.Add(System.Net.IPAddress.Parse(address));
        return app.UseForwardedHeaders(options);
    }
}
