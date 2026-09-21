using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.Logging;
using SalesSupport.Common.MasterData;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Areas.Admin.Models;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;

namespace SalesSupport.Portal.Web.Services;

/// <summary>管理画面の保存結果です。</summary>
public enum ToolAdminOutcome
{
    /// <summary>対象区画を保存しました。</summary>
    Saved,
    /// <summary>入力が条件を満たしません。</summary>
    InvalidInput,
    /// <summary>ほかの操作で更新されていたため保存しませんでした。</summary>
    Conflict,
    /// <summary>対象を取得できません。</summary>
    Unavailable
}

/// <summary>保存結果と項目エラーです。</summary>
/// <param name="Outcome">保存の判定結果です。</param>
/// <param name="Errors">入力不正時の項目エラーです。</param>
public sealed record ToolAdminResult(ToolAdminOutcome Outcome, ValidationResult Errors)
{
    /// <summary>項目エラーを伴わない結果を生成します。</summary>
    public static ToolAdminResult From(ToolAdminOutcome outcome) => new(outcome, new([]));

    /// <summary>項目エラーを伴う入力不正の結果を生成します。</summary>
    public static ToolAdminResult Invalid(params FieldError[] errors) => new(ToolAdminOutcome.InvalidInput, new(errors));
}

/// <summary>ツール編集画面の区画をまとめた表示情報です。</summary>
/// <param name="ToolName">対象ツール名です。</param>
/// <param name="ToolType">対象の提供種別です。</param>
/// <param name="Basic">基本情報区画です。</param>
/// <param name="Versions">バージョン更新情報区画です。</param>
public sealed record ToolEditContext(string ToolName, string ToolType, ToolBasicSectionViewModel Basic, ToolVersionSectionViewModel Versions);

/// <summary>A001 ツール管理のツール選択・基本情報・バージョン更新情報を扱います。</summary>
public interface IToolAdminService
{
    /// <summary>非公開を含むすべてのツールから、絞り込み条件に一致する行を返します。</summary>
    Task<IReadOnlyList<ToolSelectionItem>> GetSelectionAsync(ToolFilterInput filter, CancellationToken ct = default);

    /// <summary>ツールカテゴリの選択肢を表示順で返します。</summary>
    Task<IReadOnlyList<CategoryOption>> GetCategoriesAsync(CancellationToken ct = default);

    /// <summary>担当者に指定できる有効なシステム管理者を返します。</summary>
    Task<IReadOnlyList<UserOption>> GetOwnersAsync(CancellationToken ct = default);

    /// <summary>修正担当者に指定できる有効な利用者を返します。</summary>
    Task<IReadOnlyList<UserOption>> GetUsersAsync(CancellationToken ct = default);

    /// <summary>提出された全行のUpdateCountを確認し、表示順を一括保存します。</summary>
    Task<ToolAdminResult> SaveOrderAsync(IReadOnlyList<ToolOrderRow> rows, CancellationToken ct = default);

    /// <summary>ツール編集画面の基本情報・バージョン区画を取得します。</summary>
    Task<ToolEditContext?> GetEditContextAsync(string toolId, ToolVersionInput? versionEditor, CancellationToken ct = default);

    /// <summary>基本情報を保存します。担当者は有効なシステム管理者だけを受け付けます。</summary>
    Task<ToolAdminResult> SaveBasicAsync(string toolId, ToolBasicInput input, CancellationToken ct = default);

    /// <summary>バージョン履歴を追加します。現在のバージョンは変更しません。</summary>
    Task<ToolAdminResult> CreateVersionAsync(string toolId, ToolVersionInput input, CancellationToken ct = default);

    /// <summary>バージョン履歴を更新します。現在のバージョンは変更しません。</summary>
    Task<ToolAdminResult> UpdateVersionAsync(string toolId, ToolVersionInput input, CancellationToken ct = default);

    /// <summary>削除条件を満たす場合だけバージョン履歴を削除します。</summary>
    Task<ToolAdminResult> DeleteVersionAsync(string toolId, int toolHistoryId, int updateCount, CancellationToken ct = default);

    /// <summary>現在のバージョンを切り替えます。対象集合の相違も競合として扱います。</summary>
    Task<ToolAdminResult> SetCurrentVersionAsync(string toolId, ToolCurrentVersionInput input, CancellationToken ct = default);

    /// <summary>編集欄へ読み込むバージョン履歴を返します。</summary>
    Task<ToolVersionInput?> GetVersionEditorAsync(string toolId, int toolHistoryId, CancellationToken ct = default);
}

/// <summary>区画ごとに独立した保存単位で更新し、ほかの区画の項目を変更しません。</summary>
public sealed class ToolAdminService(PortalDbContext db, ICodeMasterReader codes, IActivityLogger activity) : IToolAdminService
{
    /// <summary>担当者未設定の選択肢は設けないため、絞り込みは登録済みの担当者だけを対象にします。</summary>
    public async Task<IReadOnlyList<ToolSelectionItem>> GetSelectionAsync(ToolFilterInput filter, CancellationToken ct = default)
    {
        var query = from tool in db.Tools.AsNoTracking()
                    join category in db.ToolCategories.AsNoTracking() on tool.CategoryId equals category.CategoryId
                    join owner in db.Users.AsNoTracking() on tool.OwnerUserId equals owner.Id
                    select new { Tool = tool, category.CategoryName, CategorySortOrder = category.SortOrder, OwnerName = owner.DisplayName };
        if (filter.CategoryId is { } categoryId) query = query.Where(x => x.Tool.CategoryId == categoryId);
        if (filter.OwnerUserId is { } ownerUserId) query = query.Where(x => x.Tool.OwnerUserId == ownerUserId);
        if (!string.IsNullOrEmpty(filter.Status)) query = query.Where(x => x.Tool.Status == filter.Status);

        return await query
            .OrderBy(x => x.CategorySortOrder).ThenBy(x => x.Tool.SortOrder).ThenBy(x => x.Tool.ToolName).ThenBy(x => x.Tool.ToolId)
            .Select(x => new ToolSelectionItem(x.Tool.ToolId, x.Tool.SortOrder, x.CategoryName, x.Tool.ToolName, x.OwnerName,
                x.Tool.Status,
                db.ToolVersionHistories.Where(h => h.ToolId == x.Tool.ToolId && h.IsCurrent).Select(h => h.Version).FirstOrDefault() ?? "",
                x.Tool.UpdateCount))
            .ToListAsync(ct);
    }

    /// <summary>カテゴリはソート順、同順位は名称で並べます。</summary>
    public async Task<IReadOnlyList<CategoryOption>> GetCategoriesAsync(CancellationToken ct = default) =>
        await db.ToolCategories.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.CategoryName)
            .Select(x => new CategoryOption(x.CategoryId, x.CategoryName)).ToListAsync(ct);

    /// <summary>担当者に指定できるのは有効なシステム管理者だけです。</summary>
    public async Task<IReadOnlyList<UserOption>> GetOwnersAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking().Where(x => x.IsActive && x.RoleCode == "ADMIN").OrderBy(x => x.DisplayName)
            .Select(x => new UserOption(x.Id, x.DisplayName)).ToListAsync(ct);

    /// <summary>修正担当者には有効な利用者を指定できます。</summary>
    public async Task<IReadOnlyList<UserOption>> GetUsersAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.DisplayName)
            .Select(x => new UserOption(x.Id, x.DisplayName)).ToListAsync(ct);

    /// <summary>並べ替えの成否を操作ログへ残します。</summary>
    public async Task<ToolAdminResult> SaveOrderAsync(IReadOnlyList<ToolOrderRow> rows, CancellationToken ct = default)
    {
        var result = await ApplyOrderAsync(rows, ct);
        await WriteAsync("TOOL_ORDER_UPDATE", null, result, ct);
        return result;
    }

    /// <summary>提出された集合のいずれかが競合した場合は一括保存を拒否します。</summary>
    private async Task<ToolAdminResult> ApplyOrderAsync(IReadOnlyList<ToolOrderRow> rows, CancellationToken ct)
    {
        var targets = rows.Where(x => !string.IsNullOrWhiteSpace(x.ToolId)).ToList();
        if (targets.Count == 0) return ToolAdminResult.Invalid(new("Rows", "REQUIRED", "並べ替える対象がありません。"));
        if (targets.Select(x => x.ToolId).Distinct(StringComparer.Ordinal).Count() != targets.Count)
            return ToolAdminResult.Invalid(new("Rows", "INVALID_INPUT", "同じツールが重複しています。"));
        if (targets.Any(x => x.SortOrder < 0))
            return ToolAdminResult.Invalid(new("Rows", "INVALID_INPUT", "表示順には0以上の数値を入力してください。"));

        var toolIds = targets.Select(x => x.ToolId!).ToList();
        var tools = await db.Tools.Where(x => toolIds.Contains(x.ToolId)).ToListAsync(ct);
        if (tools.Count != targets.Count) return ToolAdminResult.From(ToolAdminOutcome.Conflict);

        foreach (var tool in tools)
        {
            var target = targets.Single(x => x.ToolId == tool.ToolId);
            if (tool.UpdateCount != target.UpdateCount) return ToolAdminResult.From(ToolAdminOutcome.Conflict);
            tool.SortOrder = target.SortOrder;
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return ToolAdminResult.From(ToolAdminOutcome.Conflict); }
        return ToolAdminResult.From(ToolAdminOutcome.Saved);
    }

    /// <summary>基本情報とバージョン区画の表示情報をまとめて取得します。</summary>
    public async Task<ToolEditContext?> GetEditContextAsync(string toolId, ToolVersionInput? versionEditor, CancellationToken ct = default)
    {
        var tool = await db.Tools.AsNoTracking().SingleOrDefaultAsync(x => x.ToolId == toolId, ct);
        if (tool is null) return null;

        var basic = new ToolBasicSectionViewModel
        {
            Input = new ToolBasicInput
            {
                ToolName = tool.ToolName,
                CategoryId = tool.CategoryId,
                OwnerUserId = tool.OwnerUserId,
                ToolSummary = tool.ToolSummary,
                Remarks = tool.Remarks,
                Status = tool.Status,
                UpdateCount = tool.UpdateCount
            },
            ToolType = tool.ToolType,
            Categories = await GetCategoriesAsync(ct),
            Owners = await GetOwnersAsync(ct),
            Statuses = await codes.GetOptionsAsync("TOOL_STATUS", ct)
        };
        return new ToolEditContext(tool.ToolName, tool.ToolType, basic, await BuildVersionSectionAsync(toolId, versionEditor, ct));
    }

    /// <summary>管理画面では現在のバージョンより新しい履歴も含め、全件を降順で表示します。</summary>
    private async Task<ToolVersionSectionViewModel> BuildVersionSectionAsync(string toolId, ToolVersionInput? editor, CancellationToken ct)
    {
        var rows = await (from history in db.ToolVersionHistories.AsNoTracking()
                          join user in db.Users.AsNoTracking() on history.ModifiedByUserId equals user.Id
                          where history.ToolId == toolId
                          select new
                          {
                              history.ToolHistoryId,
                              history.Version,
                              history.ReleasedAt,
                              history.ChangeDescription,
                              history.IsCurrent,
                              history.UpdateCount,
                              ModifiedByName = user.DisplayName
                          }).ToListAsync(ct);

        var ordered = rows.OrderByDescending(x => x.Version, VersionOrder.Instance).ToList();
        var maximum = ordered.FirstOrDefault();
        var versions = ordered.Select(x => new ToolVersionRow(x.ToolHistoryId, x.Version, x.ReleasedAt, x.ModifiedByName,
            x.ChangeDescription, x.IsCurrent,
            // 削除できるのはバージョン番号が最大で、現在のバージョンでなく、履歴が2件以上ある場合だけです。
            CanDelete: maximum is not null && x.ToolHistoryId == maximum.ToolHistoryId && !x.IsCurrent && ordered.Count >= 2,
            x.UpdateCount)).ToList();

        return new ToolVersionSectionViewModel
        {
            Versions = versions,
            CurrentHistoryId = rows.SingleOrDefault(x => x.IsCurrent)?.ToolHistoryId,
            Editor = editor,
            Users = await GetUsersAsync(ct)
        };
    }

    /// <summary>基本情報の保存の成否を操作ログへ残します。</summary>
    public async Task<ToolAdminResult> SaveBasicAsync(string toolId, ToolBasicInput input, CancellationToken ct = default)
    {
        var result = await ApplyBasicAsync(toolId, input, ct);
        await WriteAsync("TOOL_UPDATE", toolId, result, ct,
            result.Outcome == ToolAdminOutcome.Saved && input.Status is not null ? [new ActivityChange("Status", null, input.Status)] : null);
        return result;
    }

    /// <summary>担当者行をロックして有効なシステム管理者であることを確認してから保存します。</summary>
    private async Task<ToolAdminResult> ApplyBasicAsync(string toolId, ToolBasicInput input, CancellationToken ct)
    {
        List<FieldError> errors = [];
        if (CommonValidation.ValidateText(nameof(ToolBasicInput.ToolName), input.ToolName, 200, required: true) is { } nameError) errors.Add(nameError);
        if (CommonValidation.ValidateText(nameof(ToolBasicInput.ToolSummary), input.ToolSummary, 1000, required: false) is { } summaryError) errors.Add(summaryError);
        if (CommonValidation.ValidateText(nameof(ToolBasicInput.Remarks), input.Remarks, 2000, required: false) is { } remarksError) errors.Add(remarksError);
        if (input.Status is not ("PUBLIC" or "PRIVATE" or "HIDDEN")) errors.Add(new(nameof(ToolBasicInput.Status), "INVALID_INPUT", "状態を選択してください。"));
        if (errors.Count != 0) return new(ToolAdminOutcome.InvalidInput, new(errors));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var owner = await db.LockUserAsync(input.OwnerUserId, ct);
            if (owner is null || !owner.IsActive || owner.RoleCode != "ADMIN")
                return ToolAdminResult.Invalid(new(nameof(ToolBasicInput.OwnerUserId), "INVALID_INPUT", "担当者には有効なシステム管理者を選択してください。"));
            if (!await db.ToolCategories.AsNoTracking().AnyAsync(x => x.CategoryId == input.CategoryId, ct))
                return ToolAdminResult.Invalid(new(nameof(ToolBasicInput.CategoryId), "INVALID_INPUT", "カテゴリを選択してください。"));

            var tool = await db.Tools.SingleOrDefaultAsync(x => x.ToolId == toolId, ct);
            if (tool is null) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);
            if (tool.UpdateCount != input.UpdateCount) return ToolAdminResult.From(ToolAdminOutcome.Conflict);
            // 資料提供型を一般公開にする場合は、参考資料が1件以上必要です。
            if (tool.ToolType == "DOCUMENT" && input.Status == "PUBLIC"
                && !await db.ToolFiles.AsNoTracking().AnyAsync(x => x.ToolId == toolId && x.FileCategory == "REFERENCE", ct))
                return ToolAdminResult.Invalid(new(nameof(ToolBasicInput.Status), "INVALID_INPUT", "一般公開にするには参考資料を1件以上登録してください。"));

            tool.ToolName = input.ToolName!;
            tool.CategoryId = input.CategoryId;
            tool.OwnerUserId = input.OwnerUserId;
            tool.ToolSummary = CommonValidation.Normalize(input.ToolSummary);
            tool.Remarks = CommonValidation.Normalize(input.Remarks);
            tool.Status = input.Status!;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToolAdminResult.From(ToolAdminOutcome.Saved);
        }
        catch (DbUpdateConcurrencyException) { return ToolAdminResult.From(ToolAdminOutcome.Conflict); }
    }

    /// <summary>履歴追加の成否を操作ログへ残します。</summary>
    public async Task<ToolAdminResult> CreateVersionAsync(string toolId, ToolVersionInput input, CancellationToken ct = default)
    {
        var result = await ApplyVersionAsync(toolId, input, create: true, ct);
        await WriteAsync("VERSION_CREATE", toolId, result, ct);
        return result;
    }

    /// <summary>履歴更新の成否を操作ログへ残します。</summary>
    public async Task<ToolAdminResult> UpdateVersionAsync(string toolId, ToolVersionInput input, CancellationToken ct = default)
    {
        var result = await ApplyVersionAsync(toolId, input, create: false, ct);
        await WriteAsync("VERSION_UPDATE", toolId, result, ct);
        return result;
    }

    /// <summary>対象ツール行をロックし、履歴の追加または更新を確定します。</summary>
    private async Task<ToolAdminResult> ApplyVersionAsync(string toolId, ToolVersionInput input, bool create, CancellationToken ct)
    {
        List<FieldError> errors = [];
        if (!CommonValidation.IsVersion(input.Version))
            errors.Add(new(nameof(ToolVersionInput.Version), "INVALID_INPUT", "バージョンは0～99の数字3組をピリオドで区切って入力してください。"));
        if (input.ReleasedAt is null) errors.Add(new(nameof(ToolVersionInput.ReleasedAt), "REQUIRED", "リリース日を入力してください。"));
        if (CommonValidation.ValidateText(nameof(ToolVersionInput.ChangeDescription), input.ChangeDescription, 2000, required: true) is { } descriptionError)
            errors.Add(descriptionError);
        if (errors.Count != 0) return new(ToolAdminOutcome.InvalidInput, new(errors));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var tool = await db.LockToolAsync(toolId, ct);
            if (tool is null) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);
            if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == input.ModifiedByUserId && x.IsActive, ct))
                return ToolAdminResult.Invalid(new(nameof(ToolVersionInput.ModifiedByUserId), "INVALID_INPUT", "修正担当者には有効な利用者を選択してください。"));
            // 新規登録では除外対象がないため、IDENTITYが採番しない0を比較対象にします。
            var editingHistoryId = input.ToolHistoryId ?? 0;
            if (await db.ToolVersionHistories.AsNoTracking()
                    .AnyAsync(x => x.ToolId == toolId && x.Version == input.Version && x.ToolHistoryId != editingHistoryId, ct))
                return ToolAdminResult.Invalid(new(nameof(ToolVersionInput.Version), "INVALID_INPUT", "同じバージョンが既に登録されています。"));

            if (create)
            {
                // 新規履歴は現在のバージョンにしません。切替は専用の保存操作で行います。
                db.ToolVersionHistories.Add(new ToolVersionHistory
                {
                    ToolId = toolId,
                    Version = input.Version!,
                    ModifiedByUserId = input.ModifiedByUserId,
                    ChangeDescription = input.ChangeDescription!,
                    ReleasedAt = input.ReleasedAt!.Value,
                    IsCurrent = false
                });
            }
            else
            {
                var history = await db.ToolVersionHistories.SingleOrDefaultAsync(x => x.ToolHistoryId == editingHistoryId && x.ToolId == toolId, ct);
                if (history is null) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);
                if (history.UpdateCount != input.UpdateCount) return ToolAdminResult.From(ToolAdminOutcome.Conflict);
                history.Version = input.Version!;
                history.ModifiedByUserId = input.ModifiedByUserId;
                history.ChangeDescription = input.ChangeDescription!;
                history.ReleasedAt = input.ReleasedAt!.Value;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToolAdminResult.From(ToolAdminOutcome.Saved);
        }
        catch (DbUpdateConcurrencyException) { return ToolAdminResult.From(ToolAdminOutcome.Conflict); }
    }

    /// <summary>履歴削除の成否を操作ログへ残します。</summary>
    public async Task<ToolAdminResult> DeleteVersionAsync(string toolId, int toolHistoryId, int updateCount, CancellationToken ct = default)
    {
        var result = await ApplyVersionDeleteAsync(toolId, toolHistoryId, updateCount, ct);
        await WriteAsync("VERSION_DELETE", toolId, result, ct);
        return result;
    }

    /// <summary>最大バージョン・現在版でない・2件以上を同時に確認してから削除します。</summary>
    private async Task<ToolAdminResult> ApplyVersionDeleteAsync(string toolId, int toolHistoryId, int updateCount, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var tool = await db.LockToolAsync(toolId, ct);
            if (tool is null) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);

            var histories = await db.ToolVersionHistories.Where(x => x.ToolId == toolId).ToListAsync(ct);
            var history = histories.SingleOrDefault(x => x.ToolHistoryId == toolHistoryId);
            if (history is null) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);
            if (history.UpdateCount != updateCount) return ToolAdminResult.From(ToolAdminOutcome.Conflict);
            if (histories.Count < 2) return ToolAdminResult.Invalid(new("Version", "INVALID_INPUT", "履歴が1件のため削除できません。"));
            if (history.IsCurrent) return ToolAdminResult.Invalid(new("Version", "INVALID_INPUT", "現在のバージョンは削除できません。別の履歴を現在のバージョンとして保存してください。"));
            var maximum = histories.Where(x => CommonValidation.IsVersion(x.Version)).OrderByDescending(x => x.Version, VersionOrder.Instance).FirstOrDefault();
            if (maximum is null || maximum.ToolHistoryId != toolHistoryId)
                return ToolAdminResult.Invalid(new("Version", "INVALID_INPUT", "バージョン番号が最大の履歴だけを削除できます。"));

            db.ToolVersionHistories.Remove(history);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToolAdminResult.From(ToolAdminOutcome.Saved);
        }
        catch (DbUpdateConcurrencyException) { return ToolAdminResult.From(ToolAdminOutcome.Conflict); }
    }

    /// <summary>現在のバージョン切替の成否を操作ログへ残します。</summary>
    public async Task<ToolAdminResult> SetCurrentVersionAsync(string toolId, ToolCurrentVersionInput input, CancellationToken ct = default)
    {
        var result = await ApplyCurrentVersionAsync(toolId, input, ct);
        await WriteAsync("VERSION_SELECT", toolId, result, ct);
        return result;
    }

    /// <summary>旧版をfalseで保存してから新版をtrueで保存し、同じトランザクションで確定します。</summary>
    private async Task<ToolAdminResult> ApplyCurrentVersionAsync(string toolId, ToolCurrentVersionInput input, CancellationToken ct)
    {
        if (input.ToolHistoryId <= 0) return ToolAdminResult.Invalid(new(nameof(ToolCurrentVersionInput.ToolHistoryId), "REQUIRED", "現在のバージョンを選択してください。"));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var tool = await db.LockToolAsync(toolId, ct);
            if (tool is null) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);

            var histories = await db.ToolVersionHistories.Where(x => x.ToolId == toolId).ToListAsync(ct);
            // 別要求による履歴の追加・削除・更新も競合として扱い、選択を確定しません。
            if (histories.Count != input.Rows.Count
                || histories.Any(history => !input.Rows.Any(row => row.ToolHistoryId == history.ToolHistoryId && row.UpdateCount == history.UpdateCount)))
                return ToolAdminResult.From(ToolAdminOutcome.Conflict);

            var target = histories.SingleOrDefault(x => x.ToolHistoryId == input.ToolHistoryId);
            if (target is null) return ToolAdminResult.From(ToolAdminOutcome.Unavailable);
            if (target.IsCurrent)
            {
                await transaction.CommitAsync(ct);
                return ToolAdminResult.From(ToolAdminOutcome.Saved);
            }

            // 条件付き一意制約に合わせ、旧版の解除を先に確定します。
            foreach (var history in histories.Where(x => x.IsCurrent)) history.IsCurrent = false;
            await db.SaveChangesAsync(ct);
            target.IsCurrent = true;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ToolAdminResult.From(ToolAdminOutcome.Saved);
        }
        catch (DbUpdateConcurrencyException) { return ToolAdminResult.From(ToolAdminOutcome.Conflict); }
    }

    /// <summary>別ツールの履歴は編集欄へ読み込みません。</summary>
    public async Task<ToolVersionInput?> GetVersionEditorAsync(string toolId, int toolHistoryId, CancellationToken ct = default) =>
        await db.ToolVersionHistories.AsNoTracking().Where(x => x.ToolId == toolId && x.ToolHistoryId == toolHistoryId)
            .Select(x => new ToolVersionInput
            {
                ToolHistoryId = x.ToolHistoryId,
                Version = x.Version,
                ReleasedAt = x.ReleasedAt,
                ModifiedByUserId = x.ModifiedByUserId,
                ChangeDescription = x.ChangeDescription,
                UpdateCount = x.UpdateCount
            }).SingleOrDefaultAsync(ct);

    /// <summary>保存結果を操作ログへ記録します。ログ失敗で保存結果は変更しません。</summary>
    private async Task WriteAsync(string eventType, string? toolId, ToolAdminResult result, CancellationToken ct,
        IReadOnlyList<ActivityChange>? changes = null)
    {
        var succeeded = result.Outcome == ToolAdminOutcome.Saved;
        await activity.WriteAsync(new ActivityEvent(eventType, succeeded ? "SUCCESS" : "FAILURE",
            succeeded ? null : result.Outcome switch
            {
                ToolAdminOutcome.Conflict => "CONFLICT",
                ToolAdminOutcome.InvalidInput => "INVALID_INPUT",
                _ => "TOOL_UNAVAILABLE"
            }, toolId is null ? null : "TOOL", toolId, changes), ct);
    }

    /// <summary>バージョン番号を数値の組として比較する並べ替え用の比較子です。</summary>
    private sealed class VersionOrder : IComparer<string>
    {
        /// <summary>共有できる比較子の実体です。</summary>
        public static readonly VersionOrder Instance = new();

        /// <summary>形式が不正な値は最小として扱い、並べ替えを失敗させません。</summary>
        public int Compare(string? x, string? y)
        {
            var left = CommonValidation.IsVersion(x);
            var right = CommonValidation.IsVersion(y);
            if (!left || !right) return left == right ? 0 : left ? 1 : -1;
            return CommonValidation.CompareVersions(x!, y!);
        }
    }
}
