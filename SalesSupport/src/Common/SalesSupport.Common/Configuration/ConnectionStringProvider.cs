using Microsoft.Extensions.Configuration;

namespace SalesSupport.Common.Configuration;

/// <summary>DB接続文字列の取得元です。Portalと各ツールは自身の設定ではなく本サービスから接続文字列を受け取ります。</summary>
public interface IConnectionStringProvider
{
    /// <summary>営業支援システムの業務DBへ接続する文字列を返します。</summary>
    string SalesSupportDatabase { get; }
}

/// <summary>共通設定ファイルの接続文字列を保持します。値を例外本文や画面へ出しません。</summary>
public sealed class ConnectionStringProvider : IConnectionStringProvider
{
    /// <summary>共通設定ファイル上の接続文字列名です。</summary>
    public const string ConnectionName = "SalesSupport";

    /// <summary>起動時に接続文字列を取得します。未設定・空白は構成エラーとして起動を止めます。</summary>
    /// <param name="configuration">共通設定ファイルから構成した設定。</param>
    public ConnectionStringProvider(IConfiguration configuration)
    {
        var value = configuration.GetConnectionString(ConnectionName);
        if (string.IsNullOrWhiteSpace(value)) throw new ConfigurationException($"ConnectionStrings:{ConnectionName}");
        SalesSupportDatabase = value;
    }

    /// <summary>共通設定ファイルから取得した業務DBの接続文字列です。</summary>
    public string SalesSupportDatabase { get; }
}
