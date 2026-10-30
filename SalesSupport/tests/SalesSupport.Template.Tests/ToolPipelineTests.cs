using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SalesSupport.Common.DateTime;
using Xunit;

namespace SalesSupport.Template.Tests;

/// <summary>Commonが組み込む要求パイプラインを実際の<c>Program.cs</c>で起動し、未認証の要求とCSRFトークンのない更新要求がControllerの処理へ進まないことを確認します。</summary>
/// <remarks>テストサーバー内で完結し、DB・Portal・SMTPへは接続しません。共有Cookieでの入場とDBを使う利用制御は<c>portal</c>起動プロファイルで確認します。</remarks>
public sealed class ToolPipelineTests : IDisposable
{
    /// <summary>連携モードの共通設定へ書くPortalのURLです。ログイン画面への誘導先の確認に使います。</summary>
    private const string PortalBaseUrl = "https://portal.example.invalid/";

    /// <summary>画面のフォームが持つCSRFトークンを取り出す正規表現です。</summary>
    private static readonly Regex TokenPattern = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");

    private readonly string root = Path.Combine(Path.GetTempPath(), "ss-tool-pipeline-" + Guid.NewGuid().ToString("N"));

    /// <summary>連携モードの検証用フォルダーを削除します。</summary>
    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { /* 未作成・検証環境の残存は無視します。 */ }
    }

    /// <summary>連携モードでは、未認証の保護対象URLをControllerへ渡さずPortalのログイン画面へ誘導し、応答にCommonのセキュリティヘッダーを付けます。</summary>
    [WindowsFact]
    public async Task UnauthenticatedRequestIsRedirectedToPortalLogin()
    {
        await using var factory = CreatePortalModeFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(PortalBaseUrl + "account/login?returnUrl=%2F", response.Headers.Location?.OriginalString);
        // ヘッダーの値はCommonのテストで確認します。ここではCommonのパイプラインを通ったことだけを確かめます。
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    /// <summary>HTTP運用では、HTTPの要求をHTTPSへ誘導せず、Portalのログイン画面（http）へ案内します。</summary>
    [WindowsFact]
    public async Task HttpTransportModeKeepsHttpRequests()
    {
        const string httpPortal = "http://portal.example.invalid/";
        await using var factory = CreatePortalModeFactory("Http", httpPortal);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(httpPortal + "account/login?returnUrl=%2F", response.Headers.Location?.OriginalString);
    }

    /// <summary>既定のHTTPSでは、HTTPの要求をControllerへ渡さずHTTPSへ誘導します。</summary>
    [WindowsFact]
    public async Task HttpsTransportModeRedirectsHttpRequestsToHttps()
    {
        await using var factory = CreatePortalModeFactory();
        // テストサーバーは待ち受けアドレスを持たないため、HTTPSへの誘導先ポートを設定で与えます。
        await using var configured = factory.WithWebHostBuilder(builder => builder.UseSetting("HTTPS_PORT", "443"));
        using var client = configured.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("https://localhost/", response.Headers.Location?.OriginalString);
    }

    /// <summary>単独開発では、トークンのない更新要求を400で拒否し、画面から受け取ったトークンを付けた同じ要求はControllerまで届きます。</summary>
    [Fact]
    public async Task PostWithoutAntiforgeryTokenIsRejected()
    {
        await using var factory = CreateStandaloneFactory();
        using var client = CreateClient(factory);

        // 入口の表示で利用が許可されることを確かめ、CSRFトークン（Cookieとフォームの値）を受け取ります。
        var entry = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        var token = TokenPattern.Match(await entry.Content.ReadAsStringAsync()).Groups[1].Value;
        Assert.NotEmpty(token);

        // Cookieだけが送られ、フォームのトークンがない要求です。他サイトから送らせた要求に相当します。
        var withoutToken = await client.PostAsync("/execute", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);

        // 入力が空でもトークンがあればControllerへ届き、入力エラーを表示した入力画面を返します。
        var withToken = await client.PostAsync("/execute", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
    }

    /// <summary>画面要求で例外が起きた場合は、JSONではなく共通エラー画面を返し、問い合わせ番号だけを示します。</summary>
    [Fact]
    public async Task UnhandledScreenErrorRendersCommonErrorView()
    {
        await using var factory = CreateStandaloneFactory(services => services.AddScoped<IBusinessDateProvider, ThrowingBusinessDateProvider>());
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("お問い合わせ番号", html);
        // 例外の詳細は画面へ出しません。
        Assert.DoesNotContain("ThrowingBusinessDateProvider", html);
    }

    /// <summary>共通エラー画面の確認で、入口画面の準備を失敗させる代替です。</summary>
    private sealed class ThrowingBusinessDateProvider : IBusinessDateProvider
    {
        /// <summary>業務日付の取得で例外を発生させます。</summary>
        public Task<DateOnly> GetTodayAsync(CancellationToken ct = default) => throw new InvalidOperationException("検証用の障害です。");
    }

    /// <summary>リダイレクトを自動で追わないHTTPSのクライアントを作成します。CSRFのCookieはSecure属性のため、HTTPSの要求でだけ送られます。</summary>
    /// <param name="factory">要求を送るテスト用のホストです。</param>
    /// <returns>Cookieを保持するクライアントです。</returns>
    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    /// <summary>IISの標準配置（<c>apps/T001</c>と<c>config</c>）を一時フォルダーに作り、連携モードでツールを起動します。</summary>
    /// <param name="transportMode">共通設定ファイルへ書く通信方式です。NULLでは既定のHTTPSになります。</param>
    /// <param name="portalBaseUrl">共通設定ファイルへ書くPortalのURLです。NULLでは既定のHTTPSのURLです。</param>
    /// <returns>連携モードで起動するテスト用のホストです。</returns>
    private WebApplicationFactory<Program> CreatePortalModeFactory(string? transportMode = null, string? portalBaseUrl = null)
    {
        var contentRoot = Path.Combine(root, "apps", "T001");
        foreach (var folder in new[] { contentRoot, Path.Combine(root, "config"), Path.Combine(root, "keys"), Path.Combine(root, "storage", "temp") })
            Directory.CreateDirectory(folder);
        var settings = new
        {
            // 接続しないダミーです。未認証の要求はDBを参照する前にログインへ誘導されます。
            ConnectionStrings = new { SalesSupport = "Data Source=tool-pipeline-test;" },
            SalesSupport = new
            {
                EnvironmentCode = "DEVELOPMENT",
                Site = new { Name = "結合テストサイト", TransportMode = transportMode },
                // ToolIdとアプリ名は本来アプリごとの環境変数で与えます。テストではプロセス全体の環境変数を変えないよう共通設定ファイルへ書きます。
                Application = new { Name = "結合テスト", ToolId = "T001" },
                Portal = new { BaseUrl = portalBaseUrl ?? PortalBaseUrl },
                DataProtection = new { KeyDirectory = "../keys" },
                // 保存領域はコンテンツルートとテストの出力フォルダーの外に置く必要があります。
                Storage = new { TemporaryRoot = "../storage/temp" }
            }
        };
        File.WriteAllText(Path.Combine(root, "config", "salessupport.common.json"), JsonSerializer.Serialize(settings));
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // 既定の位置（コンテンツルートから../../config/salessupport.common.json）で共通設定ファイルを読ませます。
            builder.UseContentRoot(contentRoot);
            // 起動プロファイルportalと同じく連携モードを明示し、実行環境に残った単独開発の環境変数を引き継ぎません。
            builder.UseSetting("SalesSupport:Tool:ExecutionMode", "Portal");
        });
    }

    /// <summary>起動プロファイル<c>standalone</c>と同じホスト設定でツールを起動します。共通設定ファイル・DB・共有鍵を使いません。</summary>
    /// <param name="configure">検証用に差し替えるサービスです。差替えが不要な場合はNULLにします。</param>
    /// <returns>単独開発で起動するテスト用のホストです。</returns>
    private static WebApplicationFactory<Program> CreateStandaloneFactory(Action<IServiceCollection>? configure = null) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        // 単独開発はホストのDevelopmentだけで受け付けます。
        builder.UseEnvironment("Development");
        builder.UseSetting("SalesSupport:Tool:ExecutionMode", "Standalone");
        builder.UseSetting("SalesSupport:Application:ToolId", "T001");
        builder.UseSetting("SalesSupport:Application:Name", "結合テスト");
        builder.UseSetting("SalesSupport:Site:Name", "結合テストサイト");
        builder.ConfigureTestServices(services =>
        {
            // テストサーバーは接続元を設定しないため、ブラウザーで開く場合と同じ自端末からの要求として扱います。
            services.AddSingleton<IStartupFilter, LocalClientStartupFilter>();
            // ユーザープロファイルへ鍵を保存しないよう、CSRFトークンの保護にはメモリー上の鍵を使います。
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            configure?.Invoke(services);
        });
    });

    /// <summary>テストサーバーの要求を自端末（ループバック）からの要求として扱います。単独開発の利用条件をブラウザーで開く場合と同じにするためだけに使います。</summary>
    private sealed class LocalClientStartupFilter : IStartupFilter
    {
        /// <summary>ホストのパイプラインより前に、接続元を自端末として設定する処理を加えます。</summary>
        /// <param name="next">ホスト側のパイプライン構成です。</param>
        /// <returns>接続元の設定を先頭に加えたパイプライン構成です。</returns>
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    /// <summary>Windowsでだけ実行するテストです。連携モードは共有鍵をWindows DPAPIで保護するため、ほかのOSでは起動できません。</summary>
    private sealed class WindowsFactAttribute : FactAttribute
    {
        /// <summary>Windows以外では理由を示してスキップします。</summary>
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "連携モードは共有鍵をWindows DPAPIで保護するため、Windowsだけで実行します。";
        }
    }
}
