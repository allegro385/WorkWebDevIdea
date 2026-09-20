using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Data;

namespace SalesSupport.Common.Configuration;

/// <summary>設定値を露出せず必要なキーを型付きで取得します。</summary>
public interface ISystemSettingsReader
{
    /// <summary>必須のサイト公開状態を返します。</summary>
    Task<string> GetPublicationStatusAsync(CancellationToken ct = default);
    /// <summary>非公開時の案内を返します。</summary>
    Task<string> GetPrivateMessageAsync(CancellationToken ct = default);
    /// <summary>開発用業務日付を返し、本番ではDBを参照しません。</summary>
    Task<DateOnly?> GetBusinessDateAsync(CancellationToken ct = default);
}

/// <summary>必要なDB設定を要求内で共有する読取り処理です。</summary>
public sealed class DatabaseSettingsReader(IDbContextFactory<CommonDbContext> factory, IOptions<CommonOptions> options) : ISystemSettingsReader
{
    private readonly Dictionary<(string Category, string Key), string?> cache = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>未登録・NULL・未知の公開状態を拒否します。</summary>
    public async Task<string> GetPublicationStatusAsync(CancellationToken ct = default)
    {
        var value = await ReadAsync("SITE", "PUBLICATION_STATUS", ct);
        return value is "PUBLIC" or "PRIVATE" ? value : throw new ConfigurationException("SITE/PUBLICATION_STATUS");
    }

    /// <summary>案内未設定時は安全な既定文を返します。</summary>
    public async Task<string> GetPrivateMessageAsync(CancellationToken ct = default) => await ReadAsync("SITE", "PRIVATE_MESSAGE", ct) ?? "メンテナンス中です。";

    /// <summary>開発時だけ日付形式を完全一致で検証します。</summary>
    public async Task<DateOnly?> GetBusinessDateAsync(CancellationToken ct = default)
    {
        if (options.Value.EnvironmentCode == "PRODUCTION") return null;
        var value = await ReadAsync("TEST", "BUSINESS_DATE", ct);
        if (value is null) return null;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : throw new ConfigurationException("TEST/BUSINESS_DATE");
    }

    /// <summary>並行要求内でも同じContextを共有せず、取得成功だけをキャッシュします。</summary>
    private async Task<string?> ReadAsync(string category, string key, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue((category, key), out var value)) return value;
            await using var db = await factory.CreateDbContextAsync(ct);
            value = await db.Settings.AsNoTracking().Where(x => x.SettingCategory == category && x.SettingKey == key).Select(x => x.SettingValue).SingleOrDefaultAsync(ct);
            cache[(category, key)] = value;
            return value;
        }
        finally { gate.Release(); }
    }
}
