using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Data;
using SalesSupport.Common.MasterData;

namespace SalesSupport.SampleEstimate.Web.Services;

/// <summary>現在の公開状態をコードマスタの表示名で返します。</summary>
public interface IToolStatusService
{
    /// <summary>対象ツールの状態をDBから読み、未定義なら表示せずに失敗させます。</summary>
    Task<string> GetStatusNameAsync(CancellationToken ct = default);
}

/// <summary>Commonの読取り専用モデルとコード表示契約を使う例です。利用許可は共通認可へ委ねます。</summary>
public sealed class ToolStatusService(IDbContextFactory<CommonDbContext> factory, ICodeMasterReader codes,
    IOptions<CommonOptions> options) : IToolStatusService
{
    /// <summary>単独開発はDB未確認と表示し、通常はDBの現在値を表示名へ変換して不整合を拒否します。</summary>
    public async Task<string> GetStatusNameAsync(CancellationToken ct = default)
    {
        if (options.Value.IsStandaloneTool) return "単独開発（DB確認なし）";
        await using var db = await factory.CreateDbContextAsync(ct);
        var status = await db.Tools.AsNoTracking().Where(x => x.ToolId == options.Value.ToolId)
            .Select(x => x.Status).SingleOrDefaultAsync(ct);
        if (status is null) throw new ConfigurationException("Tools/Status");
        var code = await codes.FindAsync("TOOL_STATUS", status, ct);
        return code?.CodeName ?? throw new ConfigurationException("CodeMaster/TOOL_STATUS");
    }
}
