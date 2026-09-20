using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.Configuration;

/// <summary>ホストから共通基盤へ渡す非秘密の設定です。</summary>
public sealed class CommonOptions
{
    public ApplicationKind Kind { get; set; }
    public string EnvironmentCode { get; set; } = "";
    public string ApplicationName { get; set; } = "";
    public string? ToolId { get; set; }
    public string PortalBaseUrl { get; set; } = "";
    public string KeyDirectory { get; set; } = "";
}
