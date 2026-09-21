using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Logging;
using SalesSupport.Common.Mail;
using SalesSupport.Common.MasterData;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;
using SalesSupport.Portal.Web.Models;

namespace SalesSupport.Portal.Web.Services;

/// <summary>問い合わせ受付の判定結果です。</summary>
public enum InquiryOutcome
{
    /// <summary>保存とメール送信が完了し、問い合わせIDを表示できます。</summary>
    Accepted,
    /// <summary>入力が条件を満たしません。採番・保存・送信は行っていません。</summary>
    InvalidInput,
    /// <summary>同じ送信IDで既に送信を開始しています。</summary>
    DuplicateSubmission,
    /// <summary>保存または送信に失敗しました。受付済みとして扱いません。</summary>
    Failed
}

/// <summary>受付結果と、入力不正時の項目エラーです。</summary>
/// <param name="Outcome">受付の判定結果です。</param>
/// <param name="InquiryId">受付できた場合の問い合わせIDです。</param>
/// <param name="Errors">入力不正時の項目エラーです。</param>
public sealed record InquiryAcceptance(InquiryOutcome Outcome, string? InquiryId, ValidationResult Errors)
{
    /// <summary>項目エラーと問い合わせIDを伴わない結果を生成します。</summary>
    public static InquiryAcceptance From(InquiryOutcome outcome) => new(outcome, null, new([]));
}

/// <summary>受け付ける1件分の入力です。送信者は検証済みの本人から決定します。</summary>
/// <param name="UserId">送信者本人のユーザーIDです。</param>
/// <param name="IsAdmin">限定公開ツールを対象として選択できるかどうかです。</param>
/// <param name="CategoryCode">汎用コードマスタのINQUIRY_CATEGORYの値です。</param>
/// <param name="Target">`PORTAL`、`OTHER`または`TOOL:`とToolIdで表した対象です。</param>
/// <param name="Content">問い合わせ本文です。</param>
/// <param name="AttachmentContent">添付ファイルの内容です。添付なしではnullです。</param>
/// <param name="AttachmentName">添付ファイルの元の名前です。</param>
public sealed record InquirySubmission(Guid UserId, bool IsAdmin, string? CategoryCode, string? Target, string? Content,
    Stream? AttachmentContent, string? AttachmentName);

/// <summary>問い合わせの選択肢取得と受付処理を扱います。</summary>
public interface IInquiryService
{
    /// <summary>汎用コードマスタからカテゴリの選択肢を取得します。</summary>
    Task<IReadOnlyList<CodeOption>> GetCategoriesAsync(CancellationToken ct = default);

    /// <summary>ポータルサイト、選択できるツールおよびその他で対象の選択肢を構成します。</summary>
    Task<IReadOnlyList<InquiryTargetOption>> GetTargetsAsync(bool isAdmin, CancellationToken ct = default);

    /// <summary>添付ファイルの許可拡張子と容量上限の案内文を取得します。</summary>
    Task<string> GetAttachmentHintAsync(CancellationToken ct = default);

    /// <summary>採番・保存・メール送信を順に行い、送信できなかった場合はレコードを削除します。</summary>
    Task<InquiryAcceptance> SubmitAsync(InquirySubmission submission, CancellationToken ct = default);
}

/// <summary>採番を保存トランザクションの外で行い、送信失敗時に受付を残さない受付処理です。</summary>
public sealed class InquiryService(PortalDbContext db, ICodeMasterReader codes, IFileStorage storage, IUploadPolicyProvider policies,
    IMailSender mail, IMailTemplateRenderer templates, IBusinessDateProvider businessDate, IInquiryIdAllocator allocator,
    IActivityLogger activity) : IInquiryService
{
    /// <summary>添付ファイルの入力項目名です。項目エラーの表示先に使用します。</summary>
    public const string AttachmentField = "Attachment";

    /// <summary>問い合わせカテゴリの選択肢を表示順で返します。</summary>
    public Task<IReadOnlyList<CodeOption>> GetCategoriesAsync(CancellationToken ct = default) =>
        codes.GetOptionsAsync("INQUIRY_CATEGORY", ct);

    /// <summary>非公開ツールは選択肢へ表示しません。限定公開ツールは管理者にだけ表示します。</summary>
    public async Task<IReadOnlyList<InquiryTargetOption>> GetTargetsAsync(bool isAdmin, CancellationToken ct = default)
    {
        var tools = await (from tool in db.Tools.AsNoTracking()
                           join category in db.ToolCategories.AsNoTracking() on tool.CategoryId equals category.CategoryId
                           where tool.Status == "PUBLIC" || isAdmin && tool.Status == "PRIVATE"
                           orderby category.SortOrder, tool.SortOrder, tool.ToolName, tool.ToolId
                           select new { tool.ToolId, tool.ToolName }).ToListAsync(ct);

        List<InquiryTargetOption> targets = [new("PORTAL", "ポータルサイト")];
        targets.AddRange(tools.Select(x => new InquiryTargetOption("TOOL:" + x.ToolId, x.ToolName)));
        targets.Add(new("OTHER", "その他"));
        return targets;
    }

    /// <summary>アップロード条件を取得できない場合は例外とし、案内を省略しません。</summary>
    public async Task<string> GetAttachmentHintAsync(CancellationToken ct = default)
    {
        var policy = await policies.GetAsync(UploadPurpose.InquiryAttachment, null, ct);
        var megabytes = policy.MaxFileSizeBytes / 1_000_000m;
        return $"添付できるファイルは1件です。{string.Join("、", policy.Extensions)}（最大{megabytes:0.#}MB）";
    }

    /// <summary>受付の成否を操作ログへ残します。ログ失敗で受付結果は変更しません。</summary>
    public async Task<InquiryAcceptance> SubmitAsync(InquirySubmission submission, CancellationToken ct = default)
    {
        var acceptance = await AcceptAsync(submission, ct);
        var succeeded = acceptance.Outcome == InquiryOutcome.Accepted;
        await activity.WriteAsync(new ActivityEvent("INQUIRY_CREATE", succeeded ? "SUCCESS" : "FAILURE",
            succeeded ? null : acceptance.Outcome == InquiryOutcome.InvalidInput ? "INVALID_INPUT" : "SEND_FAILED",
            succeeded ? "INQUIRY" : null, acceptance.InquiryId), ct);
        return acceptance;
    }

    /// <summary>検証、一時添付、採番、保存、送信の順に進め、一時添付は必ず削除します。</summary>
    private async Task<InquiryAcceptance> AcceptAsync(InquirySubmission submission, CancellationToken ct)
    {
        var category = await codes.FindAsync("INQUIRY_CATEGORY", submission.CategoryCode ?? "", ct);
        var (targetType, toolId) = ParseTarget(submission.Target);
        List<FieldError> errors = [];
        if (category is null) errors.Add(new(nameof(InquiryInput.CategoryCode), "INVALID_INPUT", "カテゴリを選択してください。"));
        if (targetType is null || targetType == "TOOL" && !await IsSelectableToolAsync(toolId, submission.IsAdmin, ct))
            errors.Add(new(nameof(InquiryInput.Target), "INVALID_INPUT", "対象を選択してください。"));
        if (CommonValidation.ValidateText(nameof(InquiryInput.Content), submission.Content, 2000, required: true) is { } contentError)
            errors.Add(contentError);

        TemporaryFileHandle? attachment = null;
        try
        {
            if (submission.AttachmentContent is not null)
            {
                try
                {
                    attachment = await storage.SaveTemporaryAsync(
                        new TemporaryFileRequest(UploadPurpose.InquiryAttachment, null, submission.AttachmentName ?? ""), submission.AttachmentContent, ct);
                }
                catch (UploadRejectedException exception) { errors.Add(exception.Error with { Field = AttachmentField }); }
            }
            if (errors.Count != 0) return new(InquiryOutcome.InvalidInput, null, new(errors));

            var sender = await db.Users.AsNoTracking().Where(x => x.Id == submission.UserId)
                .Select(x => new { x.Email, x.IsActive }).SingleOrDefaultAsync(ct);
            if (sender is null || !sender.IsActive || !CommonValidation.IsEmail(sender.Email)) return InquiryAcceptance.From(InquiryOutcome.Failed);

            var recipients = await ResolveBccAsync(targetType!, toolId, ct);
            var assignee = targetType == "TOOL" ? await ResolveAssigneeAsync(toolId!, ct) : null;
            var targetLabel = await DescribeTargetAsync(targetType!, toolId, ct);

            // 採番は保存トランザクションの外側で行い、失敗しても確保した番号を戻しません。
            var inquiryId = await allocator.AllocateAsync(await businessDate.GetTodayAsync(ct), ct);
            var inquiry = new Inquiry
            {
                InquiryId = inquiryId,
                CategoryCode = category!.CodeValue,
                TargetType = targetType!,
                ToolId = toolId,
                SubmittedByUserId = submission.UserId,
                Content = submission.Content!,
                Status = "ACTION_REQUIRED",
                AssigneeUserId = assignee
            };
            db.Inquiries.Add(inquiry);
            // 1件の登録を1つの保存単位として確定してから、メールを一度だけ送信します。
            await db.SaveChangesAsync(ct);

            var content = templates.Render(MailTemplateKeys.InquiryReceipt, new Dictionary<string, string>
            {
                ["問い合わせID"] = inquiryId,
                ["カテゴリ"] = category.CodeName,
                ["対象"] = targetLabel,
                ["内容"] = submission.Content!
            });
            var attachments = attachment is null ? null : new List<TemporaryFileHandle> { attachment };
            var sent = await mail.SendAsync(new MailRequest([sender.Email!], [], recipients, content.Subject, content.Body, attachments), ct);
            if (sent.Outcome != DeliveryOutcome.Succeeded)
            {
                await RemoveAsync(inquiry, ct);
                return InquiryAcceptance.From(InquiryOutcome.Failed);
            }
            return new(InquiryOutcome.Accepted, inquiryId, new([]));
        }
        finally
        {
            // 送信の成否にかかわらず一時添付を削除します。削除失敗はCommon側で記録します。
            if (attachment is not null) await attachment.DisposeAsync();
        }
    }

    /// <summary>送信できなかった問い合わせを削除します。削除失敗の復旧処理は設けません。</summary>
    private async Task RemoveAsync(Inquiry inquiry, CancellationToken ct)
    {
        try
        {
            db.Inquiries.Remove(inquiry);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); }
    }

    /// <summary>対象の指定を種別とツールIDへ分解します。未選択・形式不正ではnullを返します。</summary>
    private static (string? TargetType, string? ToolId) ParseTarget(string? target) => target switch
    {
        "PORTAL" => ("PORTAL", null),
        "OTHER" => ("OTHER", null),
        not null when target.StartsWith("TOOL:", StringComparison.Ordinal) && CommonValidation.IsCode(target[5..], 20) => ("TOOL", target[5..]),
        _ => (null, null)
    };

    /// <summary>選択肢として表示できる状態のツールかどうかを再確認します。</summary>
    private async Task<bool> IsSelectableToolAsync(string? toolId, bool isAdmin, CancellationToken ct) =>
        toolId is not null && await db.Tools.AsNoTracking()
            .AnyAsync(x => x.ToolId == toolId && (x.Status == "PUBLIC" || isAdmin && x.Status == "PRIVATE"), ct);

    /// <summary>ツール担当者、または有効なシステム管理者全員をBCCの宛先にします。</summary>
    private async Task<IReadOnlyList<string>> ResolveBccAsync(string targetType, string? toolId, CancellationToken ct)
    {
        if (targetType == "TOOL" && toolId is not null)
        {
            var owner = await (from tool in db.Tools.AsNoTracking()
                               join user in db.Users.AsNoTracking() on tool.OwnerUserId equals user.Id
                               where tool.ToolId == toolId && user.IsActive
                               select user.Email).SingleOrDefaultAsync(ct);
            if (CommonValidation.IsEmail(owner)) return [owner!];
        }
        return await db.Users.AsNoTracking().Where(x => x.IsActive && x.RoleCode == "ADMIN" && x.Email != null)
            .Select(x => x.Email!).ToListAsync(ct);
    }

    /// <summary>担当者には有効なシステム管理者だけを初期設定します。</summary>
    private async Task<Guid?> ResolveAssigneeAsync(string toolId, CancellationToken ct) =>
        await (from tool in db.Tools.AsNoTracking()
               join user in db.Users.AsNoTracking() on tool.OwnerUserId equals user.Id
               where tool.ToolId == toolId && user.IsActive && user.RoleCode == "ADMIN"
               select (Guid?)user.Id).SingleOrDefaultAsync(ct);

    /// <summary>メール本文へ記載する対象の表示名を決定します。</summary>
    private async Task<string> DescribeTargetAsync(string targetType, string? toolId, CancellationToken ct)
    {
        if (targetType == "PORTAL") return "ポータルサイト";
        if (targetType == "OTHER") return "その他";
        var name = await db.Tools.AsNoTracking().Where(x => x.ToolId == toolId).Select(x => x.ToolName).SingleOrDefaultAsync(ct);
        return name ?? "";
    }
}
