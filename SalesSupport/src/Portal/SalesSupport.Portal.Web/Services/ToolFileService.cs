using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Logging;
using SalesSupport.Portal.Web.Data;

namespace SalesSupport.Portal.Web.Services;

/// <summary>認可済みのダウンロード内容です。物理パスは含めません。</summary>
/// <param name="Content">呼出元が破棄する読取りStreamです。</param>
/// <param name="FileName">添付名として使用する安全化済みのファイル名です。</param>
/// <param name="IsApplication">配布アプリかどうかです。参考資料と利用ログの集計対象を区別します。</param>
public sealed record ToolFileDownload(Stream Content, string FileName, bool IsApplication);

/// <summary>ツールに所属するファイルの取得を扱います。認可は呼出元が事前に判定します。</summary>
public interface IToolFileService
{
    /// <summary>ToolIdとFileIdの所属を照合し、保存領域内のファイルを開きます。</summary>
    Task<ToolFileDownload?> OpenAsync(string toolId, int fileId, CancellationToken ct = default);
}

/// <summary>DBの相対パスだけでファイルを解決し、配布アプリの取得要求を一度記録します。</summary>
public sealed class ToolFileService(PortalDbContext db, IFileStorage storage, IUsageLogger usage) : IToolFileService
{
    /// <summary>別ツールのファイルIDを指定した要求は取得できません。</summary>
    public async Task<ToolFileDownload?> OpenAsync(string toolId, int fileId, CancellationToken ct = default)
    {
        var file = await db.ToolFiles.AsNoTracking().Where(x => x.ToolId == toolId && x.FileId == fileId)
            .Select(x => new { x.FileCategory, x.OriginalFileName, x.RelativePath, x.Extension, x.FileSizeBytes })
            .SingleOrDefaultAsync(ct);
        if (file is null) return null;

        var reference = new StoredFile(file.RelativePath, file.Extension, file.FileSizeBytes, file.OriginalFileName);
        var content = await storage.OpenReadAsync(reference, ct);
        var isApplication = file.FileCategory == "APP";
        // 配布アプリの取得要求だけを利用者数の集計対象として記録します。参考資料は同じイベントにしません。
        if (isApplication) await usage.WriteAsync(new UsageEvent(toolId, "DESKTOP_DOWNLOAD", "SUCCESS"), ct);
        return new ToolFileDownload(content, SafeFileName(file.OriginalFileName, file.Extension), isApplication);
    }

    /// <summary>経路区切りと制御文字を除いた添付名へ整えます。空になる場合は拡張子だけの名前にします。</summary>
    private static string SafeFileName(string originalFileName, string extension)
    {
        var name = Path.GetFileName(originalFileName.Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Where(x => !char.IsControl(x) && x != '"' && !invalid.Contains(x)).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "download" + extension : safe;
    }
}
