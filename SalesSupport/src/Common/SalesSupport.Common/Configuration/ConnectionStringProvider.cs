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
    private readonly string? connectionString;
    /// <summary>共通設定ファイル上の接続文字列名です。</summary>
    public const string ConnectionName = "SalesSupport";

    /// <summary>通常は未設定で起動を止めます。単独開発の場合だけ未設定のまま保持し、DB利用時に拒否します。</summary>
    /// <param name="configuration">共通設定ファイルから構成した設定。</param>
    /// <param name="requireConnectionString">通常はtrue。検証済み単独開発の登録時だけfalseを指定します。</param>
    public ConnectionStringProvider(IConfiguration configuration, bool requireConnectionString = true)
    {
        var value = configuration.GetConnectionString(ConnectionName);
        if (requireConnectionString && string.IsNullOrWhiteSpace(value)) throw new ConfigurationException($"ConnectionStrings:{ConnectionName}");
        connectionString = value;
    }

    /// <summary>共通設定ファイルから取得した業務DBの接続文字列です。</summary>
    public string SalesSupportDatabase => !string.IsNullOrWhiteSpace(connectionString) ? connectionString
        : throw new ConfigurationException($"ConnectionStrings:{ConnectionName}");
}
