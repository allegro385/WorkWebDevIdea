using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DependencyInjection;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>要求パイプラインをCommonへ集約した結果として、全ホストが同じセキュリティヘッダーとCSRFフィルターを得ることを確認します。</summary>
[Collection(CommonConfigPathCollection.Name)]
public sealed class CommonPipelineTests : IDisposable
{
    /// <summary>全応答へ付与するヘッダーと値です。[Common詳細設計]のセキュリティヘッダーを正とします。</summary>
    private static readonly (string Name, string Value)[] ExpectedHeaders =
    [
        ("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'"),
        ("X-Content-Type-Options", "nosniff"),
        ("X-Frame-Options", "DENY"),
        ("Referrer-Policy", "same-origin")
    ];

    private readonly string root = Path.Combine(Path.GetTempPath(), "ss-pipeline-" + Guid.NewGuid().ToString("N"));
    private readonly string contentRoot;

    /// <summary>標準配置（<c>apps/T001</c>と<c>config</c>・<c>keys</c>）を模した検証用フォルダーを作成します。</summary>
    public CommonPipelineTests()
    {
        contentRoot = Path.Combine(root, "apps", "T001");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "keys"));
    }

    /// <summary>検証用フォルダーを削除します。</summary>
    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { /* 検証環境の残存は無視します。 */ }
    }

    /// <summary>通常の応答へ、CSP・種類の推測禁止・枠への埋込み拒否・Referrer方針をまとめて付与します。</summary>
    [Fact]
    public async Task AppliesSecurityHeadersToResponse()
    {
        var response = await SendAsync(_ => Task.CompletedTask);
        foreach (var (name, value) in ExpectedHeaders) Assert.Equal(value, response.Headers[name].ToString());
    }

    /// <summary>共通例外処理と同じように<c>Response.Clear()</c>で応答を作り直しても、セキュリティヘッダーは残ります。</summary>
    [Fact]
    public async Task KeepsSecurityHeadersAfterResponseClear()
    {
        var response = await SendAsync(context =>
        {
            context.Response.Headers["X-Verify-Cleared"] = "1";
            context.Response.Clear();
            return Task.CompletedTask;
        });
        Assert.False(response.Headers.ContainsKey("X-Verify-Cleared"));
        foreach (var (name, value) in ExpectedHeaders) Assert.Equal(value, response.Headers[name].ToString());
    }

    /// <summary>Portalでもツールでも、Commonの登録だけで更新要求のCSRF検証がグローバルフィルターとして適用されます。</summary>
    /// <param name="kind">登録するアプリ種別です。</param>
    [Theory]
    [InlineData(ApplicationKind.Portal)]
    [InlineData(ApplicationKind.Tool)]
    public void RegistersAntiforgeryFilterForEveryHost(ApplicationKind kind)
    {
        var settings = new
        {
            ConnectionStrings = new { SalesSupport = "Data Source=verify;" },
            Portal = new { EnvironmentCode = "DEVELOPMENT" },
            SalesSupport = new
            {
                Application = new { Name = "検証", ToolId = "T001" },
                Portal = new { BaseUrl = "https://example.invalid/" },
                DataProtection = new { KeyDirectory = "../keys" }
            }
        };
        File.WriteAllText(Path.Combine(root, "config", "salessupport.common.json"), System.Text.Json.JsonSerializer.Serialize(settings));

        var services = new ServiceCollection().AddLogging();
        services.AddSalesSupportCommon(kind, new StubEnvironment { ContentRootPath = contentRoot }, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetRequiredService<IOptions<MvcOptions>>().Value.Filters, x => x is AutoValidateAntiforgeryTokenAttribute);
    }

    /// <summary>セキュリティヘッダーのミドルウェアだけのパイプラインで1要求を処理し、応答の送信開始まで進めます。</summary>
    /// <param name="inner">ミドルウェアの内側で実行する処理です。</param>
    /// <returns>送信を開始した応答です。</returns>
    private static async Task<IHttpResponseFeature> SendAsync(RequestDelegate inner)
    {
        var builder = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        builder.UseSalesSupportSecurityHeaders();
        builder.Run(inner);
        var pipeline = builder.Build();

        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());
        var response = new StartableResponseFeature();
        features.Set<IHttpResponseFeature>(response);
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(Stream.Null));

        await pipeline(new DefaultHttpContext(features));
        await response.StartAsync();
        return response;
    }

    /// <summary><c>OnStarting</c>で登録した処理を実行できる応答機能です。既定の実装は登録を無視するため差し替えます。</summary>
    private sealed class StartableResponseFeature : HttpResponseFeature
    {
        /// <summary>登録順に保持した処理です。実際のサーバーと同じく逆順で実行します。</summary>
        private readonly List<(Func<object, Task> Callback, object State)> callbacks = [];

        private bool started;

        /// <summary>応答の送信を開始したかどうかです。</summary>
        public override bool HasStarted => started;

        /// <summary>応答の送信開始前に実行する処理を登録します。</summary>
        /// <param name="callback">実行する処理です。</param>
        /// <param name="state">処理へ渡す状態です。</param>
        public override void OnStarting(Func<object, Task> callback, object state) => callbacks.Add((callback, state));

        /// <summary>登録済みの処理を実行し、応答の送信開始を表します。</summary>
        public async Task StartAsync()
        {
            for (var i = callbacks.Count - 1; i >= 0; i--) await callbacks[i].Callback(callbacks[i].State);
            started = true;
        }
    }

    /// <summary>起動環境名とコンテンツルートだけを指定するホスト情報です。</summary>
    private sealed class StubEnvironment : IHostEnvironment
    {
        /// <summary>実行環境名です。</summary>
        public string EnvironmentName { get; set; } = "Development";

        /// <summary>アプリ名です。</summary>
        public string ApplicationName { get; set; } = "Tests";

        /// <summary>共通設定ファイルの基準になるコンテンツルートです。</summary>
        public string ContentRootPath { get; set; } = "";

        /// <summary>コンテンツルートのファイルプロバイダーです。検証では参照しません。</summary>
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
