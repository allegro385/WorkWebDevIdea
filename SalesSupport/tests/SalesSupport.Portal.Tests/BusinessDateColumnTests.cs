using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Logging;
using SalesSupport.Common.Mail;
using SalesSupport.Common.MasterData;
using SalesSupport.Portal.Web.Areas.Admin.Models;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;
using SalesSupport.Portal.Web.Mail;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>画面の日付・検索・並び順が共通監査列ではなく業務列を使うことを確認します。</summary>
public sealed class BusinessDateColumnTests
{
    private static readonly Guid SenderId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    /// <summary>お知らせの登録と編集保存で、アプリケーションの実時刻をEditedAtへ設定します。</summary>
    [Fact]
    public async Task NoticeSaveSetsEditedAtFromClock()
    {
        await using var db = CreateContext();
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero));
        var service = CreateNotices(db, clock);

        Assert.Equal(NoticeSaveOutcome.Saved, (await service.SaveAsync("SYSTEM", null, new(null, "表題", "本文", true, 0))).Outcome);
        var notice = db.Notices.Single();
        Assert.Equal(clock.Now.UtcDateTime, notice.EditedAt);

        clock.Now = clock.Now.AddDays(2);
        Assert.Equal(NoticeSaveOutcome.Saved, (await service.SaveAsync("SYSTEM", null, new(notice.NoticeId, "表題2", "本文2", true, notice.UpdateCount))).Outcome);
        Assert.Equal(clock.Now.UtcDateTime, db.Notices.Single().EditedAt);
    }

    /// <summary>管理一覧とポータル表示は、EditedAtの新しい順に並べ、その日時を表示します。</summary>
    [Fact]
    public async Task NoticeListsUseEditedAt()
    {
        await using var db = CreateContext();
        db.Notices.Add(new Notice { NoticeId = 1, NoticeType = "SYSTEM", Title = "古い", Content = "本文", IsPublished = true, EditedAt = new(2026, 10, 1, 0, 0, 0) });
        db.Notices.Add(new Notice { NoticeId = 2, NoticeType = "SYSTEM", Title = "新しい", Content = "本文", IsPublished = true, EditedAt = new(2026, 10, 5, 16, 0, 0) });
        await db.SaveChangesAsync();
        var clock = new MutableClock(DateTimeOffset.UnixEpoch);

        var admin = await CreateNotices(db, clock).GetListAsync("SYSTEM", null);
        Assert.Equal([2, 1], admin.Select(x => x.NoticeId));
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 1, 0, 0, TimeSpan.FromHours(9)), admin[0].EditedAt);

        var portal = await new SystemNoticeService(db, clock).GetPublishedAsync();
        Assert.Equal([2, 1], portal.Select(x => x.NoticeId));
        Assert.Equal(new DateOnly(2026, 10, 6), portal[0].Date);
    }

    /// <summary>問い合わせ管理の期間検索・受付日表示・並び順はReceivedAtを使います。</summary>
    [Fact]
    public async Task InquirySearchUsesReceivedAt()
    {
        await using var db = CreateContext();
        db.Users.Add(new ApplicationUser
        {
            Id = SenderId, UserName = "staff001", NormalizedUserName = "STAFF001", Email = "staff@example.invalid",
            NormalizedEmail = "STAFF@EXAMPLE.INVALID", SecurityStamp = "test", ConcurrencyStamp = "test",
            DisplayName = "試験利用者", RoleCode = "A", IsActive = true
        });
        // UTC 10/31 15:30はJST 11/1、UTC 10/31 14:00はJST 10/31です。
        db.Inquiries.Add(NewInquiry("202611010001", new(2026, 10, 31, 15, 30, 0)));
        db.Inquiries.Add(NewInquiry("202610310001", new(2026, 10, 31, 14, 0, 0)));
        db.Inquiries.Add(NewInquiry("202611020001", new(2026, 11, 2, 3, 0, 0)));
        await db.SaveChangesAsync();
        var service = new InquiryAdminService(db, new EmptyCodes(), new MutableClock(DateTimeOffset.UnixEpoch), new SilentActivity());

        var oneDay = await service.SearchAsync(new InquirySearchInput { From = new(2026, 11, 1), To = new(2026, 11, 1) });
        var item = Assert.Single(oneDay);
        Assert.Equal("202611010001", item.InquiryId);
        Assert.Equal(new DateOnly(2026, 11, 1), item.ReceivedOn);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 30, 0, TimeSpan.FromHours(9)), item.ReceivedAt);

        var all = await service.SearchAsync(new InquirySearchInput());
        Assert.Equal(["202611020001", "202611010001", "202610310001"], all.Select(x => x.InquiryId));
    }

    /// <summary>同じステータスの問い合わせを指定した受付日時で作成します。</summary>
    /// <param name="inquiryId">問い合わせIDです。</param>
    /// <param name="receivedAtUtc">UTCの受付日時です。</param>
    /// <returns>検証用の問い合わせです。</returns>
    private static Inquiry NewInquiry(string inquiryId, DateTime receivedAtUtc) => new()
    {
        InquiryId = inquiryId, ReceivedAt = receivedAtUtc, CategoryCode = "QUESTION", TargetType = "PORTAL",
        SubmittedByUserId = SenderId, Content = "本文", Status = "ACTION_REQUIRED"
    };

    /// <summary>SQLやSMTPへ接続しない使い捨てのEFデータベースを作成します。</summary>
    /// <returns>インメモリーのPortalDbContextです。</returns>
    private static PortalDbContext CreateContext() => new(new DbContextOptionsBuilder<PortalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>指定した時計を使うお知らせサービスを作成します。</summary>
    /// <param name="db">検証用のContextです。</param>
    /// <param name="clock">実時刻の代替です。</param>
    /// <returns>検証用のお知らせサービスです。</returns>
    private static NoticeService CreateNotices(PortalDbContext db, IApplicationClock clock)
    {
        var templates = new MailTemplateOptions();
        NoticeMailTemplates.AddDefaults(templates);
        return new(db, clock, new RejectMail(), new MailTemplateRenderer(Options.Create(templates),
            Options.Create(new CommonOptions { ApplicationName = "試験" })), new ConfirmationStore(clock), new SilentActivity());
    }

    /// <summary>試験中に進められるUTC時刻を返す代替です。</summary>
    /// <param name="now">最初に返すUTC時刻です。</param>
    private sealed class MutableClock(DateTimeOffset now) : IApplicationClock
    {
        /// <summary>現在として返すUTC時刻です。</summary>
        public DateTimeOffset Now { get; set; } = now;

        /// <summary>設定されたUTC時刻を返します。</summary>
        public DateTimeOffset GetUtcNow() => Now;

        /// <summary>JSTへ変換します。</summary>
        public DateTimeOffset ToJst(DateTimeOffset utc) => utc.ToOffset(TimeSpan.FromHours(9));
    }

    /// <summary>メール送信を使わない検証で呼出しを検出する代替です。</summary>
    private sealed class RejectMail : IMailSender
    {
        /// <summary>送信を呼び出さないことを確認します。</summary>
        public Task<MailSendResult> SendAsync(MailRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("この検証ではメールを送信しません。");
    }

    /// <summary>コード名の解決を伴わない代替です。</summary>
    private sealed class EmptyCodes : ICodeMasterReader
    {
        /// <summary>選択肢を返しません。</summary>
        public Task<IReadOnlyList<CodeOption>> GetOptionsAsync(string codeType, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CodeOption>>([]);

        /// <summary>コードを解決しません。</summary>
        public Task<CodeOption?> FindAsync(string codeType, string codeValue, CancellationToken ct = default) => Task.FromResult<CodeOption?>(null);
    }

    /// <summary>検証で生成した操作ログを外部へ保存しません。</summary>
    private sealed class SilentActivity : IActivityLogger
    {
        /// <summary>ログの保存を省略して業務処理の結果を維持します。</summary>
        public Task<LogWriteResult> WriteAsync(ActivityEvent entry, CancellationToken ct = default) => Task.FromResult(LogWriteResult.Skipped);
    }
}
