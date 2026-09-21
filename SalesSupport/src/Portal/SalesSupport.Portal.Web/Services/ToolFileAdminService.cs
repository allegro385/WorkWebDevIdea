using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Logging;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Areas.Admin.Models;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;

namespace SalesSupport.Portal.Web.Services;

/// <summary>A001 ツール管理の提供内容区画を扱います。物理パスは画面へ渡しません。</summary>
public interface IToolFileAdminService
{
    /// <summary>提供内容区画の表示情報を取得します。</summary>
    Task<ToolContentSectionViewModel?> GetContentAsync(string toolId, ToolReferenceInput? editor, CancellationToken ct = default);

    /// <summary>Webツールのページ URLを保存します。</summary>
    Task<ToolAdminResult> SaveWebUrlAsync(string toolId, string? webAppUrl, int updateCount, CancellationToken ct = default);

    /// <summary>配布アプリを登録または差し替えます。DB更新の成功後に旧ファイルを削除します。</summary>
    Task<ToolAdminResult> SaveAppAsync(string toolId, Guid userId, Stream? content, string? fileName, CancellationToken ct = default);

    /// <summary>参考資料を追加、または表示名・ファイルを更新します。</summary>
    Task<ToolAdminResult> SaveReferenceAsync(string toolId, Guid userId, ToolReferenceInput input, Stream? content, string? fileName,
        CancellationToken ct = default);

    /// <summary>参考資料のDB参照を先に削除してから実ファイルを削除します。</summary>
    Task<ToolAdminResult> DeleteReferenceAsync(string toolId, int fileId, int updateCount, CancellationToken ct = default);

    /// <summary>提出された参考資料の集合と版を確認し、表示順を一括保存します。</summary>
    Task<ToolAdminResult> SaveReferenceOrderAsync(string toolId, IReadOnlyList<ToolFileOrderRow> rows, CancellationToken ct = default);

    /// <summary>編集欄へ読み込む参考資料を返します。</summary>
    Task<ToolReferenceInput?> GetReferenceEditorAsync(string toolId, int fileId, CancellationToken ct = default);
}

/// <summary>新ファイルの保存、DB確定、旧ファイル削除の順で更新します。</summary>
public sealed class ToolFileAdminService(PortalDbContext db, IFileStorage storage, IUploadPolicyProvider policies,
    IApplicationClock clock, IActivityLogger activity, ISystemErrorLogger errors) : IToolFileAdminService
{
    /// <summary>提供種別に応じて表示する入力欄と案内を切り替えます。</summary>
    public async Task<ToolContentSectionViewModel?> GetContentAsync(string toolId, ToolReferenceInput? editor, CancellationToken ct = default)
    {
        var tool = await db.Tools.AsNoTracking().Where(x => x.ToolId == toolId)
            .Select(x => new { x.ToolType, x.WebAppUrl, x.UpdateCount }).SingleOrDefaultAsync(ct);
        if (tool is null) return null;

        var files = await (from file in db.ToolFiles.AsNoTracking()
                           join user in db.Users.AsNoTracking() on file.UploadedByUserId equals user.Id
                           where file.ToolId == toolId
                           orderby file.SortOrder, file.FileId
                           select new
                           {
                               file.FileId,
                               file.FileCategory,
                               file.DisplayName,
                               file.OriginalFileName,
                               file.FileSizeBytes,
                               file.SortOrder,
                               file.UploadedAt,
                               file.UpdateCount,
                               UploaderName = user.DisplayName,
                               UploaderEmail = user.Email
                           }).ToListAsync(ct);

        var rows = files.Select(x => new
        {
            x.FileCategory,
            Row = new ToolFileRow(x.FileId, x.DisplayName, x.OriginalFileName, x.FileSizeBytes, x.SortOrder,
                x.UploaderName, x.UploaderEmail ?? "", ToJst(x.UploadedAt), x.UpdateCount)
        }).ToList();

        return new ToolContentSectionViewModel
        {
            ToolType = tool.ToolType,
            WebAppUrl = tool.WebAppUrl,
            ToolUpdateCount = tool.UpdateCount,
            AppFile = rows.Where(x => x.FileCategory == "APP").Select(x => x.Row).FirstOrDefault(),
            References = rows.Where(x => x.FileCategory == "REFERENCE").Select(x => x.Row).ToList(),
            Editor = editor,
            AppHint = tool.ToolType == "DESKTOP" ? await DescribeAsync(UploadPurpose.App, toolId, ct) : "",
            ReferenceHint = await DescribeAsync(UploadPurpose.Reference, toolId, ct)
        };
    }

    /// <summary>ページURLはWebツールだけに登録できます。</summary>
    public async Task<ToolAdminResult> SaveWebUrlAsync(string toolId, string? webAppUrl, int updateCount, CancellationToken ct = default)
    {
        var result = await ApplyWebUrlAsync(toolId, webAppUrl, updateCount, ct);
        await WriteAsync("TOOL_UPDATE", toolId, result, ct);
        return result;
    }

    /// <summary>ToolsのUpdateCountで競合を判定し、URL列だけを更新します。</summary>
    private async Task<ToolAdminResult> ApplyWebUrlAsync(string toolId, string? webAppUrl, int updateCount, CancellationToken ct)
    {
        var url = CommonValidation.Normalize(webAppUrl);
        if (url is not null && (url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps))
            return ToolAdminResult.Invalid(new("WebAppUrl", "INVALID_INPUT", "ページURLはhttpsで始まる2,048文字以内のURLを入力してください。"));

        var tool = await db.Tools.SingleOrDefaultAsync(x => x.ToolId == toolId, ct);
        if (tool is null) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);
        if (tool.ToolType != "WEB") return ToolAdminResult.Invalid(new("WebAppUrl", "INVALID_INPUT", "Webツール以外にはページURLを登録できません。"));
        if (tool.UpdateCount != updateCount) return ToolAdminResult.From(ToolAdminOutcome.Conflict);

        tool.WebAppUrl = url;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return ToolAdminResult.From(ToolAdminOutcome.Conflict); }
        return ToolAdminResult.From(ToolAdminOutcome.Saved);
    }

    /// <summary>配布アプリの登録・差し替えの成否を操作ログへ残します。</summary>
    public async Task<ToolAdminResult> SaveAppAsync(string toolId, Guid userId, Stream? content, string? fileName, CancellationToken ct = default)
    {
        var existing = await db.ToolFiles.AsNoTracking().AnyAsync(x => x.ToolId == toolId && x.FileCategory == "APP", ct);
        var result = await ApplyAppAsync(toolId, userId, content, fileName, ct);
        await WriteAsync(existing ? "FILE_REPLACE" : "FILE_CREATE", toolId, result, ct);
        return result;
    }

    /// <summary>新ファイルの保存後にDBを確定し、成功した場合だけ旧ファイルを削除します。</summary>
    private async Task<ToolAdminResult> ApplyAppAsync(string toolId, Guid userId, Stream? content, string? fileName, CancellationToken ct)
    {
        if (content is null || string.IsNullOrWhiteSpace(fileName))
            return ToolAdminResult.Invalid(new("AppFile", "REQUIRED", "登録するファイルを選択してください。"));
        var toolType = await db.Tools.AsNoTracking().Where(x => x.ToolId == toolId).Select(x => x.ToolType).SingleOrDefaultAsync(ct);
        if (toolType is null) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);
        if (toolType != "DESKTOP") return ToolAdminResult.Invalid(new("AppFile", "INVALID_INPUT", "配布アプリを登録できるのはデスクトップのツールだけです。"));

        StoredFile stored;
        try { stored = await storage.SavePermanentAsync(new PermanentFileRequest(UploadPurpose.App, toolId, fileName), content, ct); }
        catch (UploadRejectedException exception) { return ToolAdminResult.Invalid(exception.Error with { Field = "AppFile" }); }

        var outcome = ToolAdminOutcome.Saved;
        string? replacedPath = null;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var tool = await db.LockToolAsync(toolId, ct);
            if (tool is null) outcome = ToolAdminOutcome.Unavailable;
            else
            {
                var file = await db.ToolFiles.SingleOrDefaultAsync(x => x.ToolId == toolId && x.FileCategory == "APP", ct);
                if (file is null)
                {
                    db.ToolFiles.Add(NewFile(toolId, "APP", stored.OriginalName, stored, userId, sortOrder: 0));
                }
                else
                {
                    replacedPath = file.RelativePath;
                    file.DisplayName = stored.OriginalName;
                    Assign(file, stored, userId);
                }
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
        }
        catch (DbUpdateException) { outcome = ToolAdminOutcome.Conflict; }

        if (outcome != ToolAdminOutcome.Saved)
        {
            // DB確定に失敗した場合は新しい物理ファイルを清掃し、旧参照を維持します。
            await CleanupAsync(stored);
            db.ChangeTracker.Clear();
            return ToolAdminResult.From(outcome);
        }
        if (replacedPath is not null) await DeletePhysicalAsync(replacedPath, stored.Extension);
        return ToolAdminResult.From(ToolAdminOutcome.Saved);
    }

    /// <summary>参考資料の登録・更新の成否を操作ログへ残します。</summary>
    public async Task<ToolAdminResult> SaveReferenceAsync(string toolId, Guid userId, ToolReferenceInput input, Stream? content, string? fileName,
        CancellationToken ct = default)
    {
        var result = await ApplyReferenceAsync(toolId, userId, input, content, fileName, ct);
        await WriteAsync(input.FileId is null ? "FILE_CREATE" : content is null ? "FILE_UPDATE" : "FILE_REPLACE", toolId, result, ct);
        return result;
    }

    /// <summary>新規追加では表示名とファイル、編集では表示名だけを必須とします。</summary>
    private async Task<ToolAdminResult> ApplyReferenceAsync(string toolId, Guid userId, ToolReferenceInput input, Stream? content, string? fileName,
        CancellationToken ct)
    {
        if (CommonValidation.ValidateText(nameof(ToolReferenceInput.DisplayName), input.DisplayName, 200, required: true) is { } nameError)
            return new(ToolAdminOutcome.InvalidInput, new([nameError]));
        var hasFile = content is not null && !string.IsNullOrWhiteSpace(fileName);
        if (input.FileId is null && !hasFile)
            return ToolAdminResult.Invalid(new("ReferenceFile", "REQUIRED", "登録するファイルを選択してください。"));
        if (!await db.Tools.AsNoTracking().AnyAsync(x => x.ToolId == toolId, ct)) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);

        StoredFile? stored = null;
        if (hasFile)
        {
            try { stored = await storage.SavePermanentAsync(new PermanentFileRequest(UploadPurpose.Reference, toolId, fileName!), content!, ct); }
            catch (UploadRejectedException exception) { return ToolAdminResult.Invalid(exception.Error with { Field = "ReferenceFile" }); }
        }

        var outcome = ToolAdminOutcome.Saved;
        string? replacedPath = null;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var tool = await db.LockToolAsync(toolId, ct);
            if (tool is null) outcome = ToolAdminOutcome.Unavailable;
            else if (input.FileId is not { } fileId)
            {
                var nextOrder = await db.ToolFiles.Where(x => x.ToolId == toolId && x.FileCategory == "REFERENCE")
                    .Select(x => (int?)x.SortOrder).MaxAsync(ct) ?? -1;
                db.ToolFiles.Add(NewFile(toolId, "REFERENCE", input.DisplayName!, stored!, userId, nextOrder + 1));
            }
            else
            {
                var file = await db.ToolFiles.SingleOrDefaultAsync(x => x.ToolId == toolId && x.FileId == fileId && x.FileCategory == "REFERENCE", ct);
                if (file is null) outcome = ToolAdminOutcome.Unavailable;
                else if (file.UpdateCount != input.UpdateCount) outcome = ToolAdminOutcome.Conflict;
                else
                {
                    file.DisplayName = input.DisplayName!;
                    // 表示名だけの変更ではアップロード情報を更新しません。
                    if (stored is not null)
                    {
                        replacedPath = file.RelativePath;
                        Assign(file, stored, userId);
                    }
                }
            }
            if (outcome == ToolAdminOutcome.Saved)
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
        }
        catch (DbUpdateException) { outcome = ToolAdminOutcome.Conflict; }

        if (outcome != ToolAdminOutcome.Saved)
        {
            if (stored is not null) await CleanupAsync(stored);
            db.ChangeTracker.Clear();
            return ToolAdminResult.From(outcome);
        }
        if (replacedPath is not null && stored is not null) await DeletePhysicalAsync(replacedPath, stored.Extension);
        return ToolAdminResult.From(ToolAdminOutcome.Saved);
    }

    /// <summary>参考資料削除の成否を操作ログへ残します。</summary>
    public async Task<ToolAdminResult> DeleteReferenceAsync(string toolId, int fileId, int updateCount, CancellationToken ct = default)
    {
        var result = await ApplyReferenceDeleteAsync(toolId, fileId, updateCount, ct);
        await WriteAsync("FILE_DELETE", toolId, result, ct);
        return result;
    }

    /// <summary>DB参照の削除を確定してから実ファイルを削除します。</summary>
    private async Task<ToolAdminResult> ApplyReferenceDeleteAsync(string toolId, int fileId, int updateCount, CancellationToken ct)
    {
        string relativePath;
        string extension;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var tool = await db.LockToolAsync(toolId, ct);
            if (tool is null) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);

            var file = await db.ToolFiles.SingleOrDefaultAsync(x => x.ToolId == toolId && x.FileId == fileId && x.FileCategory == "REFERENCE", ct);
            if (file is null) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);
            if (file.UpdateCount != updateCount) return ToolAdminResult.From(ToolAdminOutcome.Conflict);
            relativePath = file.RelativePath;
            extension = file.Extension;

            db.ToolFiles.Remove(file);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return ToolAdminResult.From(ToolAdminOutcome.Conflict); }

        await DeletePhysicalAsync(relativePath, extension);
        return ToolAdminResult.From(ToolAdminOutcome.Saved);
    }

    /// <summary>参考資料の並べ替えの成否を操作ログへ残します。</summary>
    public async Task<ToolAdminResult> SaveReferenceOrderAsync(string toolId, IReadOnlyList<ToolFileOrderRow> rows, CancellationToken ct = default)
    {
        var result = await ApplyReferenceOrderAsync(toolId, rows, ct);
        await WriteAsync("FILE_ORDER_UPDATE", toolId, result, ct);
        return result;
    }

    /// <summary>対象ツール行をロックし、提出された集合と版の一致を確認してから確定します。</summary>
    private async Task<ToolAdminResult> ApplyReferenceOrderAsync(string toolId, IReadOnlyList<ToolFileOrderRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return ToolAdminResult.Invalid(new("Rows", "REQUIRED", "並べ替える対象がありません。"));
        if (rows.Any(x => x.SortOrder < 0)) return ToolAdminResult.Invalid(new("Rows", "INVALID_INPUT", "表示順には0以上の数値を入力してください。"));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var tool = await db.LockToolAsync(toolId, ct);
            if (tool is null) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);

            var files = await db.ToolFiles.Where(x => x.ToolId == toolId && x.FileCategory == "REFERENCE").ToListAsync(ct);
            if (files.Count != rows.Count
                || files.Any(file => !rows.Any(row => row.FileId == file.FileId && row.UpdateCount == file.UpdateCount)))
                return ToolAdminResult.From(ToolAdminOutcome.Conflict);

            foreach (var file in files) file.SortOrder = rows.Single(x => x.FileId == file.FileId).SortOrder;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToolAdminResult.From(ToolAdminOutcome.Saved);
        }
        catch (DbUpdateConcurrencyException) { return ToolAdminResult.From(ToolAdminOutcome.Conflict); }
    }

    /// <summary>別ツールの参考資料は編集欄へ読み込みません。</summary>
    public async Task<ToolReferenceInput?> GetReferenceEditorAsync(string toolId, int fileId, CancellationToken ct = default) =>
        await db.ToolFiles.AsNoTracking().Where(x => x.ToolId == toolId && x.FileId == fileId && x.FileCategory == "REFERENCE")
            .Select(x => new ToolReferenceInput { FileId = x.FileId, DisplayName = x.DisplayName, UpdateCount = x.UpdateCount })
            .SingleOrDefaultAsync(ct);

    /// <summary>新規のToolFiles行を作成します。アップロード情報は検証済みの管理者と実時刻で設定します。</summary>
    private ToolFile NewFile(string toolId, string category, string displayName, StoredFile stored, Guid userId, int sortOrder)
    {
        var file = new ToolFile { ToolId = toolId, FileCategory = category, DisplayName = displayName, SortOrder = sortOrder };
        Assign(file, stored, userId);
        return file;
    }

    /// <summary>保存済みファイルの情報とアップロード情報を設定します。</summary>
    private void Assign(ToolFile file, StoredFile stored, Guid userId)
    {
        file.OriginalFileName = stored.OriginalName;
        file.RelativePath = stored.RelativePath;
        file.Extension = stored.Extension;
        file.FileSizeBytes = stored.SizeBytes;
        file.UploadedByUserId = userId;
        file.UploadedAt = clock.GetUtcNow().UtcDateTime;
    }

    /// <summary>DB確定前に保存した物理ファイルを清掃します。</summary>
    private async Task CleanupAsync(StoredFile stored)
    {
        if (await storage.DeleteAsync(stored) == FileDeleteResult.Failed) await RecordCleanupFailureAsync();
    }

    /// <summary>確定済みのDB変更は戻さず、旧ファイルの削除失敗だけを記録します。</summary>
    private async Task DeletePhysicalAsync(string relativePath, string extension)
    {
        var reference = new StoredFile(relativePath, extension, 0, "");
        if (await storage.DeleteAsync(reference) == FileDeleteResult.Failed) await RecordCleanupFailureAsync();
    }

    /// <summary>清掃失敗を一度だけ記録します。再帰的な記録は行いません。</summary>
    private async Task RecordCleanupFailureAsync()
    {
        try { await errors.WriteAsync(new SystemErrorEvent(Guid.NewGuid(), "FILE_CLEANUP_FAILED")); }
        catch (Exception) { }
    }

    /// <summary>画面案内に使用するアップロード条件の説明を作成します。</summary>
    private async Task<string> DescribeAsync(UploadPurpose purpose, string toolId, CancellationToken ct)
    {
        var policy = await policies.GetAsync(purpose, toolId, ct);
        var megabytes = policy.MaxFileSizeBytes / 1_000_000m;
        return $"{string.Join("、", policy.Extensions)}（最大{megabytes:0.#}MB）";
    }

    /// <summary>保存結果を操作ログへ記録します。ログ失敗で保存結果は変更しません。</summary>
    private async Task WriteAsync(string eventType, string toolId, ToolAdminResult result, CancellationToken ct)
    {
        var succeeded = result.Outcome == ToolAdminOutcome.Saved;
        await activity.WriteAsync(new ActivityEvent(eventType, succeeded ? "SUCCESS" : "FAILURE",
            succeeded ? null : result.Outcome switch
            {
                ToolAdminOutcome.Conflict => "CONFLICT",
                ToolAdminOutcome.InvalidInput => "INVALID_INPUT",
                _ => "TOOL_UNAVAILABLE"
            }, "TOOL", toolId), ct);
    }

    /// <summary>保存されたUTC日時を画面表示用のJSTへ変換します。</summary>
    private DateTimeOffset ToJst(System.DateTime utc) => clock.ToJst(new DateTimeOffset(System.DateTime.SpecifyKind(utc, DateTimeKind.Utc)));
}
