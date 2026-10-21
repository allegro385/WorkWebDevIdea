using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.Configuration;

/// <summary>Webツールの起動モードです。</summary>
public enum ToolExecutionMode
{
    /// <summary>共通設定ファイルを読み、共有Cookie・DB認可を行う結合試験・本番のモードです。未設定時の既定です。</summary>
    Portal,

    /// <summary>共通設定ファイル・Portal・DBを使わず、画面・計算・出力だけを動かす通常のデバッグ実行用のモードです。</summary>
    Standalone
}

/// <summary>共通設定ファイルを読む前に、ホスト設定から起動モードを確定します。</summary>
public static class ToolExecutionModeSettings
{
    /// <summary>起動モードのホスト設定キーです。環境変数では<c>SalesSupport__Tool__ExecutionMode</c>です。</summary>
    public const string Key = "SalesSupport:Tool:ExecutionMode";

    /// <summary>未設定は連携モードとし、単独開発はToolかつホストの開発環境だけで受け付けます。</summary>
    /// <param name="hostConfiguration">起動プロファイル・環境変数・各アプリの設定から構成したホスト設定です。</param>
    /// <param name="kind">PortalまたはToolです。</param>
    /// <param name="environment">ホストの実行環境です。</param>
    /// <returns>検証済みの起動モードです。</returns>
    public static ToolExecutionMode Read(IConfiguration hostConfiguration, ApplicationKind kind, IHostEnvironment environment)
    {
        var value = hostConfiguration[Key];
        if (value is null || value == nameof(ToolExecutionMode.Portal)) return ToolExecutionMode.Portal;
        // 大文字小文字の揺れや数値を受け付けず、本番やPortalでの指定は設定不備として起動を止めます。
        if (value == nameof(ToolExecutionMode.Standalone) && kind == ApplicationKind.Tool && environment.IsDevelopment()) return ToolExecutionMode.Standalone;
        throw new ConfigurationException(Key);
    }
}
