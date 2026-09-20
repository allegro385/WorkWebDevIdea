namespace SalesSupport.Common.Configuration;

/// <summary>秘密値を公開せず構成の不備を通知します。</summary>
public sealed class ConfigurationException : Exception
{
    /// <summary>設定キーだけを含む例外を作成します。</summary>
    public ConfigurationException(string key) : base($"必要な設定が未設定または不正です: {key}") { }
}
