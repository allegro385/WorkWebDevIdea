using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Logging;
using SalesSupport.Common.MasterData;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Areas.Admin.Models;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Models;

namespace SalesSupport.Portal.Web.Services;

/// <summary>問い合わせ一覧の1行です。本文はプレビューだけを表示します。</summary>
/// <param name="InquiryId">問い合わせIDです。</param>
/// <param name="ReceivedOn">受付日のJST表示値です。</param>
/// <param name="ReceivedAt">受付日時のJST表示値です。</param>
/// <param name="CategoryCode">INQUIRY_CATEGORYのコード値です。</param>
/// <param name="Target">対象を表す選択値です。</param>
/// <param name="TargetLabel">対象の表示名です。</param>
/// <param name="SenderName">送信者の表示名です。</param>
/// <param name="AssigneeUserId">担当者です。未割当ではnullです。</param>
/// <param name="Status">INQUIRY_STATUSのコード値です。</param>
/// <param name="AdminNote">管理者用備考です。</param>
/// <param name="Preview">本文の先頭を省略したプレビューです。</param>
/// <param name="UpdateCount">同時更新の検出に使用する監査列です。</param>
public sealed record InquiryListItem(string InquiryId, DateOnly ReceivedOn, DateTimeOffset ReceivedAt, string CategoryCode,
    string Target, string TargetLabel, string SenderName, Guid? AssigneeUserId, string Status, string? AdminNote,
    string Preview, int UpdateCount);

/// <summary>問い合わせ詳細の参照情報です。送信者と本文は変更できません。</summary>
/// <param name="InquiryId">問い合わせIDです。</param>
/// <param name="ReceivedAt">受付日時のJST表示値です。</param>
/// <param name="SenderName">送信者の表示名です。</param>
/// <param name="SenderEmail">送信者のメールアドレスです。</param>
/// <param name="Content">本文全体です。</param>
/// <param name="TargetLabel">対象の表示名です。</param>
public sealed record InquiryDetailView(string InquiryId, DateTimeOffset ReceivedAt, string SenderName, string SenderEmail,
    string Content, string TargetLabel);

/// <summary>問い合わせ管理の保存結果です。</summary>
public enum InquiryAdminOutcome
{
    /// <summary>変更行をすべて保存しました。</summary>
    Saved,
    /// <summary>入力が条件を満たしません。1件でも不正なら全件を保存しません。</summary>
    InvalidInput,
    /// <summary>ほかの操作で更新されていたため保存しませんでした。</summary>
    Conflict,
    /// <summary>対象の問い合わせを取得できません。</summary>
    Unavailable
}

/// <summary>保存結果と項目エラーです。</summary>
/// <param name="Outcome">保存の判定結果です。</param>
/// <param name="Errors">入力不正時の項目エラーです。</param>
public sealed record InquiryAdminResult(InquiryAdminOutcome Outcome, ValidationResult Errors)
{
    /// <summary>項目エラーを伴わない結果を生成します。</summary>
    public static InquiryAdminResult From(InquiryAdminOutcome outcome) => new(outcome, new([]));

    /// <summary>項目エラーを伴う入力不正の結果を生成します。</summary>
    public static InquiryAdminResult Invalid(FieldError error) => new(InquiryAdminOutcome.InvalidInput, new([error]));
}

/// <summary>A005 問い合わせ管理の検索・参照・更新を扱います。</summary>
public interface IInquiryAdminService
{
    /// <summary>検索条件に一致する問い合わせを、要対応・対応中・検討中を優先して返します。</summary>
    Task<IReadOnlyList<InquiryListItem>> SearchAsync(InquirySearchInput input, CancellationToken ct = default);

    /// <summary>詳細画面の参照情報と現在の更新内容を返します。</summary>
    Task<(InquiryDetailView Detail, InquiryEditRow Input)?> GetAsync(string inquiryId, CancellationToken ct = default);

    /// <summary>値が変わった行だけを同一トランザクションで保存します。1件でも失敗すれば全件を保存しません。</summary>
    Task<InquiryAdminResult> SaveAsync(IReadOnlyList<InquiryEditRow> rows, CancellationToken ct = default);

    /// <summary>対象の選択肢を返します。非公開のツールは現在値として保持できるよう含めます。</summary>
    Task<IReadOnlyList<InquiryTargetOption>> GetTargetsAsync(CancellationToken ct = default);

    /// <summary>担当者に指定できる有効なシステム管理者を返します。</summary>
    Task<IReadOnlyList<UserOption>> GetAssigneesAsync(CancellationToken ct = default);
}

/// <summary>検索条件をEFでパラメーター化し、分類変更だけを操作ログへ記録します。</summary>
public sealed class InquiryAdminService(PortalDbContext db, ICodeMasterReader codes, IApplicationClock clock,
    IActivityLogger activity) : IInquiryAdminService
{
    /// <summary>一覧に表示する本文プレビューの文字数です。</summary>
    private const int PreviewLength = 100;

    /// <summary>日付はJSTの開始以上・終了翌日未満をUTCへ変換して比較します。</summary>
    public async Task<IReadOnlyList<InquiryListItem>> SearchAsync(InquirySearchInput input, CancellationToken ct = default)
    {
        var query = db.Inquiries.AsNoTracking().AsQueryable();
        if (CommonValidation.Normalize(input.InquiryId) is { } inquiryId) query = query.Where(x => x.InquiryId == inquiryId);
        if (input.From is { } from) { var fromUtc = ToUtc(from); query = query.Where(x => x.CreatedAt >= fromUtc); }
        if (input.To is { } to) { var toUtc = ToUtc(to.AddDays(1)); query = query.Where(x => x.CreatedAt < toUtc); }
        if (CommonValidation.Normalize(input.CategoryCode) is { } category) query = query.Where(x => x.CategoryCode == category);
        if (CommonValidation.Normalize(input.AdminNote) is { } note) query = query.Where(x => x.AdminNote != null && x.AdminNote.Contains(note));
        if (CommonValidation.Normalize(input.Content) is { } content) query = query.Where(x => x.Content.Contains(content));
        if (CommonValidation.Normalize(input.Status) is { } status) query = query.Where(x => x.Status == status);
        if (input.AssigneeUserId is { } assignee) query = query.Where(x => x.AssigneeUserId == assignee);
        var (targetType, targetToolId) = ParseTarget(input.Target);
        if (targetType is not null)
        {
            query = query.Where(x => x.TargetType == targetType);
            if (targetToolId is not null) query = query.Where(x => x.ToolId == targetToolId);
        }

        var rows = await (from inquiry in query
                          join sender in db.Users.AsNoTracking() on inquiry.SubmittedByUserId equals sender.Id
                          select new
                          {
                              inquiry.InquiryId,
                              inquiry.CreatedAt,
                              inquiry.CategoryCode,
                              inquiry.TargetType,
                              inquiry.ToolId,
                              ToolName = db.Tools.Where(tool => tool.ToolId == inquiry.ToolId).Select(tool => tool.ToolName).FirstOrDefault(),
                              SenderName = sender.DisplayName,
                              inquiry.AssigneeUserId,
                              inquiry.Status,
                              inquiry.AdminNote,
                              inquiry.Content,
                              inquiry.UpdateCount
                          }).ToListAsync(ct);

        return rows
            // 要対応・対応中・検討中を優先し、同順位は受付日時とIDの降順で安定させます。
            .OrderBy(x => Priority(x.Status)).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.InquiryId, StringComparer.Ordinal)
            .Select(x => new InquiryListItem(x.InquiryId, DateOnly.FromDateTime(ToJst(x.CreatedAt).DateTime), ToJst(x.CreatedAt),
                x.CategoryCode, FormatTarget(x.TargetType, x.ToolId), DescribeTarget(x.TargetType, x.ToolName), x.SenderName,
                x.AssigneeUserId, x.Status, x.AdminNote, Preview(x.Content), x.UpdateCount))
            .ToList();
    }

    /// <summary>本文全体と管理情報を返します。添付ファイルは保存していないため表示しません。</summary>
    public async Task<(InquiryDetailView Detail, InquiryEditRow Input)?> GetAsync(string inquiryId, CancellationToken ct = default)
    {
        var row = await (from inquiry in db.Inquiries.AsNoTracking()
                         join sender in db.Users.AsNoTracking() on inquiry.SubmittedByUserId equals sender.Id
                         where inquiry.InquiryId == inquiryId
                         select new
                         {
                             inquiry.InquiryId,
                             inquiry.CreatedAt,
                             inquiry.CategoryCode,
                             inquiry.TargetType,
                             inquiry.ToolId,
                             ToolName = db.Tools.Where(tool => tool.ToolId == inquiry.ToolId).Select(tool => tool.ToolName).FirstOrDefault(),
                             SenderName = sender.DisplayName,
                             SenderEmail = sender.Email,
                             inquiry.Content,
                             inquiry.AssigneeUserId,
                             inquiry.Status,
                             inquiry.AdminNote,
                             inquiry.UpdateCount
                         }).SingleOrDefaultAsync(ct);
        if (row is null) return null;

        var detail = new InquiryDetailView(row.InquiryId, ToJst(row.CreatedAt), row.SenderName, row.SenderEmail ?? "",
            row.Content, DescribeTarget(row.TargetType, row.ToolName));
        var input = new InquiryEditRow
        {
            InquiryId = row.InquiryId,
            UpdateCount = row.UpdateCount,
            CategoryCode = row.CategoryCode,
            Target = FormatTarget(row.TargetType, row.ToolId),
            AssigneeUserId = row.AssigneeUserId,
            Status = row.Status,
            AdminNote = row.AdminNote
        };
        return (detail, input);
    }

    /// <summary>管理画面では非公開のツールも選択肢へ含め、既存の関連を保持できるようにします。</summary>
    public async Task<IReadOnlyList<InquiryTargetOption>> GetTargetsAsync(CancellationToken ct = default)
    {
        var tools = await (from tool in db.Tools.AsNoTracking()
                           join category in db.ToolCategories.AsNoTracking() on tool.CategoryId equals category.CategoryId
                           orderby category.SortOrder, tool.SortOrder, tool.ToolName, tool.ToolId
                           select new { tool.ToolId, tool.ToolName }).ToListAsync(ct);
        List<InquiryTargetOption> targets = [new("PORTAL", "ポータルサイト")];
        targets.AddRange(tools.Select(x => new InquiryTargetOption("TOOL:" + x.ToolId, x.ToolName)));
        targets.Add(new("OTHER", "その他"));
        return targets;
    }

    /// <summary>担当者には有効なシステム管理者だけを選択できます。</summary>
    public async Task<IReadOnlyList<UserOption>> GetAssigneesAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking().Where(x => x.IsActive && x.RoleCode == "ADMIN").OrderBy(x => x.DisplayName)
            .Select(x => new UserOption(x.Id, x.DisplayName)).ToListAsync(ct);

    /// <summary>保存の成否と、確定した分類変更を操作ログへ残します。</summary>
    public async Task<InquiryAdminResult> SaveAsync(IReadOnlyList<InquiryEditRow> rows, CancellationToken ct = default)
    {
        var (result, changes) = await ApplySaveAsync(rows, ct);
        await activity.WriteAsync(new ActivityEvent("INQUIRY_UPDATE", result.Outcome == InquiryAdminOutcome.Saved ? "SUCCESS" : "FAILURE",
            result.Outcome switch
            {
                InquiryAdminOutcome.Saved => null,
                InquiryAdminOutcome.Conflict => "CONFLICT",
                InquiryAdminOutcome.InvalidInput => "INVALID_INPUT",
                _ => "TOOL_UNAVAILABLE"
            }, "INQUIRY", null), ct);
        // 分類変更は確定後に、問い合わせIDと変更前後のコードだけを記録します。本文・備考は記録しません。
        foreach (var change in changes)
            await activity.WriteAsync(new ActivityEvent("INQUIRY_CLASSIFICATION_UPDATE", "SUCCESS", null, "INQUIRY", change.InquiryId, change.Changes), ct);
        return result;
    }

    /// <summary>全行を再検証し、値が変わった行だけを同一トランザクションで確定します。</summary>
    private async Task<(InquiryAdminResult Result, List<ClassificationChange> Changes)> ApplySaveAsync(IReadOnlyList<InquiryEditRow> rows, CancellationToken ct)
    {
        List<ClassificationChange> changes = [];
        var targets = rows.Where(x => !string.IsNullOrWhiteSpace(x.InquiryId)).ToList();
        if (targets.Count == 0) return (InquiryAdminResult.From(InquiryAdminOutcome.Saved), changes);

        var categories = await codes.GetOptionsAsync("INQUIRY_CATEGORY", ct);
        var statuses = await codes.GetOptionsAsync("INQUIRY_STATUS", ct);
        var assignees = (await GetAssigneesAsync(ct)).Select(x => x.UserId).ToHashSet();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var row in targets)
            {
                var inquiry = await db.Inquiries.SingleOrDefaultAsync(x => x.InquiryId == row.InquiryId, ct);
                if (inquiry is null) return (InquiryAdminResult.From(InquiryAdminOutcome.Unavailable), []);

                var (targetType, toolId) = ParseTarget(row.Target);
                var note = CommonValidation.Normalize(row.AdminNote);
                var unchanged = inquiry.CategoryCode == row.CategoryCode && inquiry.TargetType == targetType && inquiry.ToolId == toolId
                    && inquiry.AssigneeUserId == row.AssigneeUserId && inquiry.Status == row.Status && inquiry.AdminNote == note;
                if (unchanged) continue;
                if (inquiry.UpdateCount != row.UpdateCount) return (InquiryAdminResult.From(InquiryAdminOutcome.Conflict), []);

                if (!categories.Any(x => x.CodeValue == row.CategoryCode))
                    return (InquiryAdminResult.Invalid(new(nameof(InquiryEditRow.CategoryCode), "INVALID_INPUT", $"{row.InquiryId}：カテゴリを選択してください。")), []);
                if (!statuses.Any(x => x.CodeValue == row.Status))
                    return (InquiryAdminResult.Invalid(new(nameof(InquiryEditRow.Status), "INVALID_INPUT", $"{row.InquiryId}：ステータスを選択してください。")), []);
                if (targetType is null || targetType == "TOOL" && !await db.Tools.AsNoTracking().AnyAsync(x => x.ToolId == toolId, ct))
                    return (InquiryAdminResult.Invalid(new(nameof(InquiryEditRow.Target), "INVALID_INPUT", $"{row.InquiryId}：対象を選択してください。")), []);
                if (row.AssigneeUserId is { } assigneeUserId && !assignees.Contains(assigneeUserId))
                    return (InquiryAdminResult.Invalid(new(nameof(InquiryEditRow.AssigneeUserId), "INVALID_INPUT", $"{row.InquiryId}：担当者には有効なシステム管理者を選択してください。")), []);
                if (CommonValidation.ValidateText(nameof(InquiryEditRow.AdminNote), note, 2000, required: false) is { } noteError)
                    return (InquiryAdminResult.Invalid(noteError with { Message = $"{row.InquiryId}：{noteError.Message}" }), []);

                if (inquiry.CategoryCode != row.CategoryCode || inquiry.TargetType != targetType || inquiry.ToolId != toolId)
                    changes.Add(new ClassificationChange(inquiry.InquiryId,
                    [
                        new ActivityChange("CategoryCode", inquiry.CategoryCode, row.CategoryCode),
                        new ActivityChange("TargetType", inquiry.TargetType, targetType),
                        new ActivityChange("ToolId", inquiry.ToolId, toolId)
                    ]));

                // 対象の変更で担当者を自動変更せず、管理者が明示した値だけを保存します。
                inquiry.CategoryCode = row.CategoryCode!;
                inquiry.TargetType = targetType!;
                inquiry.ToolId = toolId;
                inquiry.AssigneeUserId = row.AssigneeUserId;
                inquiry.Status = row.Status!;
                inquiry.AdminNote = note;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (InquiryAdminResult.From(InquiryAdminOutcome.Saved), changes);
        }
        catch (DbUpdateConcurrencyException) { return (InquiryAdminResult.From(InquiryAdminOutcome.Conflict), []); }
    }

    /// <summary>対象の選択値を種別とツールIDへ分解します。</summary>
    private static (string? TargetType, string? ToolId) ParseTarget(string? target) => target switch
    {
        "PORTAL" => ("PORTAL", null),
        "OTHER" => ("OTHER", null),
        not null when target.StartsWith("TOOL:", StringComparison.Ordinal) && CommonValidation.IsCode(target[5..], 20) => ("TOOL", target[5..]),
        _ => (null, null)
    };

    /// <summary>保存値から画面の選択値を組み立てます。</summary>
    private static string FormatTarget(string targetType, string? toolId) => targetType == "TOOL" ? "TOOL:" + toolId : targetType;

    /// <summary>対象の表示名を決定します。</summary>
    private static string DescribeTarget(string targetType, string? toolName) => targetType switch
    {
        "PORTAL" => "ポータルサイト",
        "OTHER" => "その他",
        _ => toolName ?? ""
    };

    /// <summary>要対応・対応中・検討中を優先するための並べ替えキーを返します。</summary>
    private static int Priority(string status) => status is "ACTION_REQUIRED" or "IN_PROGRESS" or "UNDER_REVIEW" ? 0 : 1;

    /// <summary>本文の先頭を一覧用に省略します。</summary>
    private static string Preview(string content) =>
        content.Length <= PreviewLength ? content : content[..PreviewLength] + "…";

    /// <summary>JSTの日付を、比較に使用するUTC日時へ変換します。</summary>
    private static System.DateTime ToUtc(DateOnly date) => date.ToDateTime(TimeOnly.MinValue).AddHours(-9);

    /// <summary>保存されたUTC日時を画面表示用のJSTへ変換します。</summary>
    private DateTimeOffset ToJst(System.DateTime utc) => clock.ToJst(new DateTimeOffset(System.DateTime.SpecifyKind(utc, DateTimeKind.Utc)));

    /// <summary>確定した分類変更1件です。</summary>
    /// <param name="InquiryId">対象の問い合わせです。</param>
    /// <param name="Changes">変更前後のコードとIDです。</param>
    private sealed record ClassificationChange(string InquiryId, IReadOnlyList<ActivityChange> Changes);
}
