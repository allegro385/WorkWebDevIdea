using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Logging;
using SalesSupport.Common.Mail;
using SalesSupport.Common.Validation;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;
using SalesSupport.Portal.Web.Mail;

namespace SalesSupport.Portal.Web.Services;

/// <summary>管理画面に表示する登録済みお知らせ1件です。</summary>
/// <param name="NoticeId">お知らせの識別子です。</param>
/// <param name="Title">表題です。</param>
/// <param name="Content">本文です。一覧では2行程度のプレビューとして表示します。</param>
/// <param name="IsPublished">公開中かどうかです。</param>
/// <param name="UpdatedAt">更新日時のJST表示値です。</param>
/// <param name="MailSentAt">最終送信成功日時のJST表示値です。成功歴がない場合はnullです。</param>
/// <param name="UpdateCount">同時更新の検出に使用する監査列です。</param>
public sealed record NoticeListItem(int NoticeId, string Title, string Content, bool IsPublished,
    DateTimeOffset UpdatedAt, DateTimeOffset? MailSentAt, int UpdateCount);

/// <summary>お知らせ編集欄の入出力です。MailSentAtは編集保存で変更しません。</summary>
/// <param name="NoticeId">更新対象の識別子です。新規登録ではnullです。</param>
/// <param name="Title">表題です。</param>
/// <param name="Content">本文です。</param>
/// <param name="IsPublished">公開状態です。新規入力時の初期値は非公開です。</param>
/// <param name="UpdateCount">取得時のUpdateCountです。新規登録では0です。</param>
public sealed record NoticeEditModel(int? NoticeId, string? Title, string? Content, bool IsPublished, int UpdateCount);

/// <summary>お知らせ保存の判定結果です。</summary>
public enum NoticeSaveOutcome
{
    /// <summary>保存しました。</summary>
    Saved,
    /// <summary>入力が条件を満たしません。</summary>
    InvalidInput,
    /// <summary>ほかの操作で更新されていたため保存しませんでした。</summary>
    Conflict,
    /// <summary>対象のお知らせを取得できません。</summary>
    Unavailable
}

/// <summary>保存結果と項目エラーです。</summary>
/// <param name="Outcome">保存の判定結果です。</param>
/// <param name="Errors">入力不正時の項目エラーです。</param>
public sealed record NoticeSaveResult(NoticeSaveOutcome Outcome, ValidationResult Errors)
{
    /// <summary>項目エラーを伴わない結果を生成します。</summary>
    public static NoticeSaveResult From(NoticeSaveOutcome outcome) => new(outcome, new([]));
}

/// <summary>送信前に確認する対象件数と宛先人数です。</summary>
/// <param name="ConfirmationId">実行時に一度だけ使用する確認IDです。</param>
/// <param name="NoticeCount">送信対象として選択したお知らせの件数です。</param>
/// <param name="RecipientCount">確認時点の送信対象人数です。</param>
public sealed record NoticeSendPlan(Guid ConfirmationId, int NoticeCount, int RecipientCount);

/// <summary>お知らせ送信の判定結果です。</summary>
public enum NoticeSendOutcome
{
    /// <summary>送信処理が正常終了しました。配達成功を保証するものではありません。</summary>
    Sent,
    /// <summary>確認内容と現在の内容が異なるため送信しませんでした。</summary>
    Conflict,
    /// <summary>送信対象者が0人のため送信しませんでした。</summary>
    NoRecipients,
    /// <summary>送信できませんでした。以前の最終送信日時は変更しません。</summary>
    Failed
}

/// <summary>お知らせの参照・保存と、確認を伴う手動送信を扱います。</summary>
public interface INoticeService
{
    /// <summary>対象種別の登録済みお知らせを更新日の新しい順で返します。</summary>
    Task<IReadOnlyList<NoticeListItem>> GetListAsync(string noticeType, string? toolId, CancellationToken ct = default);

    /// <summary>編集欄へ読み込む1件を返します。対象種別が一致しない場合はnullです。</summary>
    Task<NoticeEditModel?> GetAsync(int noticeId, string noticeType, string? toolId, CancellationToken ct = default);

    /// <summary>新規登録または更新を行います。MailSentAtと送信は変更しません。</summary>
    Task<NoticeSaveResult> SaveAsync(string noticeType, string? toolId, NoticeEditModel input, CancellationToken ct = default);

    /// <summary>送信対象と宛先人数を確定し、確認IDを発行します。</summary>
    Task<NoticeSendPlan?> PrepareSendAsync(Guid userId, string noticeType, string? toolId, IReadOnlyList<int> noticeIds, CancellationToken ct = default);

    /// <summary>確認IDを一度だけ使用して送信し、成功時だけ最終送信日時を更新します。</summary>
    Task<NoticeSendOutcome> SendAsync(Guid userId, Guid confirmationId, CancellationToken ct = default);
}

/// <summary>確認済みのスナップショットで送信し、SMTP中にDBトランザクションを保持しません。</summary>
public sealed class NoticeService(PortalDbContext db, IApplicationClock clock, IMailSender mail, IMailTemplateRenderer templates,
    IConfirmationStore confirmations, IActivityLogger activity) : INoticeService
{
    /// <summary>本文として保存できる最大文字数です。</summary>
    public const int MaxContentLength = 10000;

    /// <summary>非公開のお知らせも管理画面では確認できます。</summary>
    public async Task<IReadOnlyList<NoticeListItem>> GetListAsync(string noticeType, string? toolId, CancellationToken ct = default)
    {
        var rows = await db.Notices.AsNoTracking().Where(x => x.NoticeType == noticeType && x.ToolId == toolId)
            .OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.NoticeId)
            .Select(x => new { x.NoticeId, x.Title, x.Content, x.IsPublished, x.UpdatedAt, x.MailSentAt, x.UpdateCount })
            .ToListAsync(ct);
        return rows.Select(x => new NoticeListItem(x.NoticeId, x.Title, x.Content, x.IsPublished,
            ToJst(x.UpdatedAt), x.MailSentAt is null ? null : ToJst(x.MailSentAt.Value), x.UpdateCount)).ToList();
    }

    /// <summary>別のツール・種別のお知らせは読み込みません。</summary>
    public async Task<NoticeEditModel?> GetAsync(int noticeId, string noticeType, string? toolId, CancellationToken ct = default) =>
        await db.Notices.AsNoTracking().Where(x => x.NoticeId == noticeId && x.NoticeType == noticeType && x.ToolId == toolId)
            .Select(x => new NoticeEditModel(x.NoticeId, x.Title, x.Content, x.IsPublished, x.UpdateCount))
            .SingleOrDefaultAsync(ct);

    /// <summary>保存の成否を操作ログへ残します。</summary>
    public async Task<NoticeSaveResult> SaveAsync(string noticeType, string? toolId, NoticeEditModel input, CancellationToken ct = default)
    {
        var result = await ApplySaveAsync(noticeType, toolId, input, ct);
        var succeeded = result.Outcome == NoticeSaveOutcome.Saved;
        await activity.WriteAsync(new ActivityEvent(input.NoticeId is null ? "NOTICE_CREATE" : "NOTICE_UPDATE",
            succeeded ? "SUCCESS" : "FAILURE", succeeded ? null : FailureReasonOf(result.Outcome),
            "NOTICE", input.NoticeId?.ToString()), ct);
        return result;
    }

    /// <summary>タイトル・本文・公開状態だけを更新します。</summary>
    private async Task<NoticeSaveResult> ApplySaveAsync(string noticeType, string? toolId, NoticeEditModel input, CancellationToken ct)
    {
        List<FieldError> errors = [];
        if (CommonValidation.ValidateText(nameof(NoticeEditModel.Title), input.Title, 200, required: true) is { } titleError) errors.Add(titleError);
        if (CommonValidation.ValidateText(nameof(NoticeEditModel.Content), input.Content, MaxContentLength, required: true) is { } contentError) errors.Add(contentError);
        if (errors.Count != 0) return new(NoticeSaveOutcome.InvalidInput, new(errors));

        try
        {
            if (input.NoticeId is not { } noticeId)
            {
                db.Notices.Add(new Notice
                {
                    NoticeType = noticeType,
                    ToolId = toolId,
                    Title = input.Title!,
                    Content = input.Content!,
                    IsPublished = input.IsPublished
                });
                await db.SaveChangesAsync(ct);
                return NoticeSaveResult.From(NoticeSaveOutcome.Saved);
            }

            var notice = await db.Notices.SingleOrDefaultAsync(x => x.NoticeId == noticeId && x.NoticeType == noticeType && x.ToolId == toolId, ct);
            if (notice is null) return NoticeSaveResult.From(NoticeSaveOutcome.Unavailable);
            if (notice.UpdateCount != input.UpdateCount) return NoticeSaveResult.From(NoticeSaveOutcome.Conflict);

            // 最終送信日時は編集保存で変更しません。送信は独立した操作です。
            notice.Title = input.Title!;
            notice.Content = input.Content!;
            notice.IsPublished = input.IsPublished;
            await db.SaveChangesAsync(ct);
            return NoticeSaveResult.From(NoticeSaveOutcome.Saved);
        }
        catch (DbUpdateConcurrencyException) { return NoticeSaveResult.From(NoticeSaveOutcome.Conflict); }
    }

    /// <summary>選択件数と宛先人数を確定し、実行時の再確認に使用する情報を保持します。</summary>
    public async Task<NoticeSendPlan?> PrepareSendAsync(Guid userId, string noticeType, string? toolId, IReadOnlyList<int> noticeIds,
        CancellationToken ct = default)
    {
        var selected = noticeIds.Distinct().ToList();
        if (selected.Count == 0) return null;
        var notices = await ReadStampsAsync(noticeType, toolId, selected, ct);
        if (notices.Count != selected.Count) return null;

        var recipients = await ResolveRecipientsAsync(noticeType, toolId, ct);
        var ticket = new NoticeSendTicket(noticeType, toolId, notices, recipients.Count);
        var confirmationId = confirmations.Issue(userId, ticket);
        return confirmationId is null ? null : new NoticeSendPlan(confirmationId.Value, notices.Count, recipients.Count);
    }

    /// <summary>送信の成否を操作ログへ残します。</summary>
    public async Task<NoticeSendOutcome> SendAsync(Guid userId, Guid confirmationId, CancellationToken ct = default)
    {
        var outcome = await ApplySendAsync(userId, confirmationId, ct);
        await activity.WriteAsync(new ActivityEvent("MAIL_SEND", outcome == NoticeSendOutcome.Sent ? "SUCCESS" : "FAILURE",
            outcome switch
            {
                NoticeSendOutcome.Sent => null,
                NoticeSendOutcome.Conflict => "CONFLICT",
                NoticeSendOutcome.NoRecipients => "NO_RECIPIENTS",
                _ => "SEND_FAILED"
            }, "NOTICE", null), ct);
        return outcome;
    }

    /// <summary>確認時点と同じ対象・人数であることを確認してから一度だけ送信します。</summary>
    private async Task<NoticeSendOutcome> ApplySendAsync(Guid userId, Guid confirmationId, CancellationToken ct)
    {
        if (confirmations.Consume<NoticeSendTicket>(confirmationId, userId) is not { } ticket) return NoticeSendOutcome.Conflict;

        var noticeIds = ticket.Notices.Select(x => x.NoticeId).ToList();
        var current = await ReadStampsAsync(ticket.NoticeType, ticket.ToolId, noticeIds, ct);
        if (current.Count != ticket.Notices.Count
            || current.Any(row => ticket.Notices.All(stamp => stamp.NoticeId != row.NoticeId || stamp.UpdateCount != row.UpdateCount)))
            return NoticeSendOutcome.Conflict;

        var recipients = await ResolveRecipientsAsync(ticket.NoticeType, ticket.ToolId, ct);
        if (recipients.Count != ticket.RecipientCount) return NoticeSendOutcome.Conflict;
        if (recipients.Count == 0) return NoticeSendOutcome.NoRecipients;

        var content = await BuildMailAsync(ticket, noticeIds, ct);
        if (content is null) return NoticeSendOutcome.Conflict;

        var sent = await mail.SendAsync(new MailRequest([], [], recipients, content.Subject, content.Body), ct);
        if (sent.Outcome != DeliveryOutcome.Succeeded) return NoticeSendOutcome.Failed;

        await RecordSentAtAsync(noticeIds, ct);
        return NoticeSendOutcome.Sent;
    }

    /// <summary>送信が成功した日時だけを短いトランザクションで保存します。本文等は上書きしません。</summary>
    /// <remarks>更新に失敗しても送信済みの事実は変わらないため、メールの再送は行いません。</remarks>
    private async Task RecordSentAtAsync(IReadOnlyList<int> noticeIds, CancellationToken ct)
    {
        var sentAt = clock.GetUtcNow().UtcDateTime;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var notices = await db.Notices.Where(x => noticeIds.Contains(x.NoticeId)).ToListAsync(ct);
            // 既存日時と今回の成功日時の新しい方を採用し、古い日時へは戻しません。
            foreach (var notice in notices.Where(x => x.MailSentAt is null || x.MailSentAt < sentAt)) notice.MailSentAt = sentAt;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); }
    }

    /// <summary>送信するグループの件名と本文を、確認済みの対象から組み立てます。</summary>
    private async Task<MailContent?> BuildMailAsync(NoticeSendTicket ticket, IReadOnlyList<int> noticeIds, CancellationToken ct)
    {
        var notices = await db.Notices.AsNoTracking().Where(x => noticeIds.Contains(x.NoticeId))
            .OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.NoticeId)
            .Select(x => new { x.Title, x.Content }).ToListAsync(ct);
        var body = string.Join("\r\n", notices.Select(x => $"【{x.Title}】\r\n{x.Content}\r\n"));
        if (ticket.NoticeType != "TOOL") return templates.Render(NoticeMailTemplates.Key(ticket.NoticeType), new Dictionary<string, string> { ["お知らせ"] = body });

        var toolName = await db.Tools.AsNoTracking().Where(x => x.ToolId == ticket.ToolId).Select(x => x.ToolName).SingleOrDefaultAsync(ct);
        if (toolName is null) return null;
        return templates.Render(NoticeMailTemplates.Tool, new Dictionary<string, string> { ["ツール名"] = toolName, ["お知らせ"] = body });
    }

    /// <summary>対象種別・対象ツールに一致するお知らせのIDと版だけを読み取ります。</summary>
    private async Task<IReadOnlyList<NoticeStamp>> ReadStampsAsync(string noticeType, string? toolId, IReadOnlyList<int> noticeIds, CancellationToken ct) =>
        await db.Notices.AsNoTracking()
            .Where(x => x.NoticeType == noticeType && x.ToolId == toolId && noticeIds.Contains(x.NoticeId))
            .Select(x => new NoticeStamp(x.NoticeId, x.UpdateCount)).ToListAsync(ct);

    /// <summary>通知設定と、ツールの場合はお気に入り登録を条件に宛先を選定します。</summary>
    /// <remarks>サイト・ツールの公開範囲では宛先を絞り込みません。宛先は重複を除いてBCCへ設定します。</remarks>
    private async Task<IReadOnlyList<string>> ResolveRecipientsAsync(string noticeType, string? toolId, CancellationToken ct)
    {
        var query = from user in db.Users.AsNoTracking()
                    join preference in db.UserPreferences.AsNoTracking() on user.Id equals preference.UserId
                    where user.IsActive && user.Email != null
                    select new { user.Id, user.Email, preference.SystemNoticeMailEnabled, preference.FavoriteToolNoticeMailEnabled };
        query = noticeType == "TOOL"
            ? query.Where(x => x.FavoriteToolNoticeMailEnabled && db.UserToolFavorites.Any(f => f.UserId == x.Id && f.ToolId == toolId))
            : query.Where(x => x.SystemNoticeMailEnabled);

        var addresses = await query.Select(x => x.Email!).ToListAsync(ct);
        return addresses.Where(CommonValidation.IsEmail).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>保存失敗の区分を操作ログの理由コードへ変換します。</summary>
    private static string FailureReasonOf(NoticeSaveOutcome outcome) => outcome switch
    {
        NoticeSaveOutcome.Conflict => "CONFLICT",
        NoticeSaveOutcome.InvalidInput => "INVALID_INPUT",
        _ => "DEPENDENCY_UNAVAILABLE"
    };

    /// <summary>保存されたUTC日時を画面表示用のJSTへ変換します。</summary>
    private DateTimeOffset ToJst(System.DateTime utc) => clock.ToJst(new DateTimeOffset(System.DateTime.SpecifyKind(utc, DateTimeKind.Utc)));

    /// <summary>送信確認時に保持するお知らせの版です。</summary>
    /// <param name="NoticeId">対象のお知らせです。</param>
    /// <param name="UpdateCount">確認時点の監査列です。</param>
    public sealed record NoticeStamp(int NoticeId, int UpdateCount);

    /// <summary>送信確認時に保持する対象と宛先人数です。</summary>
    /// <param name="NoticeType">SYSTEMまたはTOOLです。</param>
    /// <param name="ToolId">TOOLの場合の対象ツールです。</param>
    /// <param name="Notices">確認時点の対象と版です。</param>
    /// <param name="RecipientCount">確認時点の宛先人数です。</param>
    private sealed record NoticeSendTicket(string NoticeType, string? ToolId, IReadOnlyList<NoticeStamp> Notices, int RecipientCount);
}
