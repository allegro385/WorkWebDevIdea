using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Data;
using SalesSupport.Common.Validation;

namespace SalesSupport.Common.MasterData;

/// <summary>画面へ返す不変のコード表示情報です。</summary>
public sealed record CodeOption(string CodeValue, string CodeName, int SortOrder, string? ColorCode);

/// <summary>組合せキーでコードを取得します。</summary>
public interface ICodeMasterReader
{
    /// <summary>表示順・コード順に選択肢を返します。</summary>
    Task<IReadOnlyList<CodeOption>> GetOptionsAsync(string codeType, CancellationToken ct = default);
    /// <summary>区分と値の組合せを検索します。</summary>
    Task<CodeOption?> FindAsync(string codeType, string codeValue, CancellationToken ct = default);
}

/// <summary>必須コードの欠落を検出し、要求内だけ結果を共有します。</summary>
public sealed class CodeMasterReader(IDbContextFactory<CommonDbContext> factory) : ICodeMasterReader
{
    private static readonly Dictionary<string, string[]> RequiredCodes = new(StringComparer.Ordinal)
    {
        ["TOOL_STATUS"] = ["PUBLIC", "PRIVATE", "HIDDEN"],
        ["SITE_STATUS"] = ["PUBLIC", "PRIVATE"],
        ["TOOL_TYPE"] = ["DESKTOP", "WEB", "DOCUMENT"],
        ["USER_ROLE"] = ["USER", "ADMIN"],
        ["INQUIRY_CATEGORY"] = ["QUESTION", "REQUEST", "OPINION", "PROBLEM", "OTHER"],
        ["INQUIRY_STATUS"] = ["ACTION_REQUIRED", "IN_PROGRESS", "UNDER_REVIEW", "COMPLETED", "NO_ACTION"]
    };
    private readonly Dictionary<string, IReadOnlyList<CodeOption>> cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>既知の区分について必須値を照合し、不正なCSSを除外します。</summary>
    public async Task<IReadOnlyList<CodeOption>> GetOptionsAsync(string codeType, CancellationToken ct = default)
    {
        if (!RequiredCodes.TryGetValue(codeType, out var required)) throw new ArgumentException("未定義のコード区分です。", nameof(codeType));
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(codeType, out var cached)) return cached;
            await using var db = await factory.CreateDbContextAsync(ct);
            var rows = await db.Codes.AsNoTracking().Where(x => x.CodeType == codeType).OrderBy(x => x.SortOrder).ThenBy(x => x.CodeValue).ToListAsync(ct);
            if (required.Any(code => !rows.Any(row => row.CodeValue == code)) || rows.Any(x => string.IsNullOrWhiteSpace(x.CodeName)))
                throw new ConfigurationException("CodeMaster/" + codeType);
            var result = rows.Select(x => new CodeOption(x.CodeValue, x.CodeName, x.SortOrder, CommonValidation.IsColor(x.ColorCode) ? x.ColorCode : null)).ToList().AsReadOnly();
            cache.Add(codeType, result);
            return result;
        }
        finally { gate.Release(); }
    }

    /// <summary>不在とDB取得失敗を区別して返します。</summary>
    public async Task<CodeOption?> FindAsync(string codeType, string codeValue, CancellationToken ct = default) => (await GetOptionsAsync(codeType, ct)).SingleOrDefault(x => x.CodeValue == codeValue);
}
