using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SalesSupport.SampleEstimate.Tests;

/// <summary>匿名モードの未ログイン要求が、本人を要求する画面で権限判定より先にPortalのログインへ誘導されることを、実際の<c>Program.cs</c>で確認します。</summary>
/// <remarks>テストサーバー内で完結し、DB・Portal・SMTPへは接続しません。誘導前にDBを参照した場合は接続できず503になるため、判定順序の誤りを検出できます。</remarks>
public sealed class AnonymousLoginPipelineTests : IDisposable
{
    /// <summary>共通設定へ書くPortalのURLです。ログイン画面への誘導先の確認に使います。</summary>
    private const string PortalBaseUrl = "https://portal.example.invalid/";

    private readonly string root = Path.Combine(Path.GetTempPath(), "ss-sample-anonymous-" + Guid.NewGuid().ToString("N"));

    /// <summary>検証用フォルダーを削除します。</summary>
    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { /* 未作成・検証環境の残存は無視します。 */ }
    }

    /// <summary>未ログインで案件一覧を開くと、共通ユーザーの権限で判定せず、戻り先付きでPortalのログインへ誘導します。</summary>
    [Fact]
    public async Task RecordsRedirectToLoginWithoutSignedInUser()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var factory = CreateAnonymousFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var response = await client.GetAsync("/records?page=2");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(PortalBaseUrl + "account/login?returnUrl=%2Frecords%3Fpage%3D2", response.Headers.Location?.OriginalString);
    }

    /// <summary>IISの標準配置（<c>apps/tools/SAMPLE</c>と<c>config</c>）を一時フォルダーに作り、匿名モードの連携モードでサンプルを起動します。</summary>
    /// <returns>匿名モードで起動するテスト用のホストです。</returns>
    private WebApplicationFactory<Program> CreateAnonymousFactory()
    {
        var contentRoot = Path.Combine(root, "apps", "tools", "SAMPLE");
        foreach (var folder in new[] { contentRoot, Path.Combine(root, "config"), Path.Combine(root, "keys"), Path.Combine(root, "storage", "temp") })
            Directory.CreateDirectory(folder);
        var settings = new
        {
            // 接続しないダミーです。未ログインの要求はDBを参照する前にログインへ誘導されます。
            ConnectionStrings = new { SalesSupport = "Data Source=sample-anonymous-test;" },
            SalesSupport = new
            {
                EnvironmentCode = "DEVELOPMENT",
                Site = new { Name = "結合テストサイト", AuthenticationMode = "Anonymous" },
                // ToolIdとアプリ名は本来アプリごとの環境変数で与えます。テストではプロセス全体の環境変数を変えないよう共通設定ファイルへ書きます。
                Application = new { Name = "見積試算", ToolId = "SAMPLE-ESTIMATE" },
                Portal = new { BaseUrl = PortalBaseUrl },
                DataProtection = new { KeyDirectory = "../keys" },
                Storage = new { TemporaryRoot = "../storage/temp" }
            }
        };
        File.WriteAllText(Path.Combine(root, "config", "salessupport.common.json"), JsonSerializer.Serialize(settings));
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Webツールの既定の位置（コンテンツルートから../../../config/salessupport.common.json）で共通設定ファイルを読ませます。
            builder.UseContentRoot(contentRoot);
            builder.UseSetting("SalesSupport:Tool:ExecutionMode", "Portal");
        });
    }
}
