using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;

namespace SalesSupport.Common.HttpClients;

/// <summary>呼出しの終了状態です。結果不明を成功へ変換しません。</summary>
public enum HttpCallStatus { Success, HttpFailure, TransportFailure, Timeout, Canceled }

/// <summary>呼出し結果です。StatusCodeは応答を受け取れた場合だけ設定します。</summary>
public sealed record HttpCallResult<T>(HttpCallStatus Status, T? Value = default, int? StatusCode = null);

/// <summary>登録済み接続先へのJSON呼出しです。任意URLの実行は受け付けません。</summary>
public interface IJsonHttpClient
{
    /// <summary>登録済みクライアントの相対パスをGETします。</summary>
    Task<HttpCallResult<TResponse>> GetAsync<TResponse>(string clientName, string relativePath, CancellationToken ct = default);
    /// <summary>副作用のある呼出しです。失敗・結果不明でも自動再送しません。</summary>
    Task<HttpCallResult<TResponse>> PostAsync<TRequest, TResponse>(string clientName, string relativePath, TRequest body, CancellationToken ct = default);
}

/// <summary>タイムアウト・通信失敗・キャンセルを区別し、応答本文をログへ出しません。</summary>
public sealed class JsonHttpClient(IHttpClientFactory factory) : IJsonHttpClient
{
    private static readonly JsonSerializerOptions Serialization = new(JsonSerializerDefaults.Web);

    /// <summary>GETも自動再試行しません。</summary>
    public Task<HttpCallResult<TResponse>> GetAsync<TResponse>(string clientName, string relativePath, CancellationToken ct = default) =>
        SendAsync<TResponse>(clientName, () => new HttpRequestMessage(HttpMethod.Get, Relative(relativePath)), ct);

    /// <summary>本文をJSONとして送信します。</summary>
    public Task<HttpCallResult<TResponse>> PostAsync<TRequest, TResponse>(string clientName, string relativePath, TRequest body, CancellationToken ct = default) =>
        SendAsync<TResponse>(clientName, () => new HttpRequestMessage(HttpMethod.Post, Relative(relativePath))
        {
            Content = JsonContent.Create(body, options: Serialization)
        }, ct);

    /// <summary>応答本文を解釈できない場合も通信失敗として扱い、成功にしません。</summary>
    private async Task<HttpCallResult<TResponse>> SendAsync<TResponse>(string clientName, Func<HttpRequestMessage> create, CancellationToken ct)
    {
        var client = factory.CreateClient(clientName);
        if (client.BaseAddress is null) throw new ConfigurationException("Http:BaseAddress/" + clientName);
        using var request = create();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (client.Timeout != System.Threading.Timeout.InfiniteTimeSpan) timeout.CancelAfter(client.Timeout);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode) return new(HttpCallStatus.HttpFailure, default, status);
            var value = await response.Content.ReadFromJsonAsync<TResponse>(Serialization, timeout.Token);
            return new(HttpCallStatus.Success, value, status);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new(HttpCallStatus.Canceled); }
        catch (OperationCanceledException) { return new(HttpCallStatus.Timeout); }
        catch (TimeoutException) { return new(HttpCallStatus.Timeout); }
        catch (HttpRequestException) { return new(HttpCallStatus.TransportFailure); }
        catch (JsonException) { return new(HttpCallStatus.TransportFailure); }
    }

    /// <summary>絶対URL・親移動・制御文字を拒否し、固定の相対パスだけを許可します。</summary>
    private static Uri Relative(string relativePath)
    {
        var decoded = Uri.UnescapeDataString(relativePath);
        if (string.IsNullOrWhiteSpace(decoded) || decoded[0] == '/' || decoded.Any(char.IsControl) || decoded.Contains('\\')
            || decoded.Contains('%') || decoded.Split('?', '#')[0].Split('/').Any(x => x is "." or "..")
            || Uri.TryCreate(decoded, UriKind.Absolute, out _) || !Uri.TryCreate(relativePath, UriKind.Relative, out var uri))
            throw new ArgumentException("接続先の相対パスが不正です。", nameof(relativePath));
        return uri;
    }
}

/// <summary>実際に外部呼出しが必要なホストだけがクライアントを登録します。</summary>
public static class HttpClientExtensions
{
    /// <summary>検証済みBaseAddressと共通タイムアウトで名前付きクライアントを登録します。</summary>
    public static IServiceCollection AddSalesSupportHttpClient(this IServiceCollection services, string name, string baseAddress)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("クライアント名が必要です。", nameof(name));
        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !baseAddress.EndsWith('/'))
            throw new ConfigurationException("Http:BaseAddress/" + name);
        services.AddHttpClient(name, (provider, client) =>
        {
            client.BaseAddress = uri;
            client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<HttpOptions>>().Value.TimeoutSeconds);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        return services;
    }
}
