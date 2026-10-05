using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SalesSupport.Common.Contracts;

namespace SalesSupport.Common.Configuration;

/// <summary>DBを使わないツールの単独開発を、明示的な開発環境に限定します。</summary>
public static class ToolPublicationSettings
{
    /// <summary>既定はDB確認ありとし、省略はCommonとホストの両方が開発環境の場合だけ受け付けます。Portalには適用しません。</summary>
    public static bool ReadCheckPublicationStatus(IConfiguration configuration, ApplicationKind kind, IHostEnvironment? environment)
    {
        if (kind != ApplicationKind.Tool) return true;
        const string key = "SalesSupport:Tool:CheckPublicationStatus";
        var value = configuration[key];
        if (value is null) return true;
        if (!bool.TryParse(value, out var check)) throw new ConfigurationException(key);
        if (!check && (configuration["Portal:EnvironmentCode"] != "DEVELOPMENT" || environment?.IsDevelopment() != true))
            throw new ConfigurationException(key);
        return check;
    }
}
