using Microsoft.EntityFrameworkCore;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Logging;
using SalesSupport.Common.Mail;
using SalesSupport.Common.MasterData;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>問い合わせの採番が、開発用の業務日付ではなく実際のJST日付で行われることを確認します。</summary>
public sealed class InquiryServiceTests
{
    private static readonly Guid SenderId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    /// <summary>UTCの日付境界をまたぐ時刻でも、JSTの受付日で採番します。</summary>
    [Fact]
    public async Task AllocatesInquiryIdWithRealJstDate()
    {
        // UTCでは10月30日、JSTでは10月31日になる時刻を使います。
        var clock = new StubClock(new DateTimeOffset(2026, 10, 30, 15, 30, 0, TimeSpan.Zero));
        var allocator = new RecordingAllocator();
        await using var db = CreateContext();
        var service = CreateService(db, clock, allocator);

        var acceptance = await service.SubmitAsync(new InquirySubmission(SenderId, "QUESTION", "PORTAL", "採番日の確認です。", null, null));

        Assert.Equal(InquiryOutcome.Accepted, acceptance.Outcome);
        Assert.Equal(new DateOnly(2026, 10, 31), allocator.Requested);
        Assert.Equal("202610310001", acceptance.InquiryId);
    }

    /// <summary>送信者と宛先だけを登録した検証用のContextを作成します。</summary>
    /// <returns>インメモリーのPortalDbContextです。</returns>
    private static PortalDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase("inquiry-" + Guid.NewGuid().ToString("N"))
            .Options;
        var db = new PortalDbContext(options);
        db.Users.Add(CreateUser(SenderId, "staff001", "A"));
        db.Users.Add(CreateUser(Guid.Parse("00000000-0000-0000-0000-0000000000a2"), "admin001", "ADMIN"));
        db.SaveChanges();
        return db;
    }

    /// <summary>Identityの必須列をそろえた検証用ユーザーを作成します。</summary>
    /// <param name="userId">内部ユーザーIDです。</param>
    /// <param name="loginId">外部連携のログインIDです。</param>
    /// <param name="roleCode">ロールコードです。</param>
    /// <returns>検証用のユーザーです。</returns>
    private static ApplicationUser CreateUser(Guid userId, string loginId, string roleCode) => new()
    {
        Id = userId, UserName = loginId, NormalizedUserName = loginId.ToUpperInvariant(),
        Email = loginId + "@example.invalid", NormalizedEmail = (loginId + "@EXAMPLE.INVALID").ToUpperInvariant(),
        EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        DisplayName = "試験利用者", RoleCode = roleCode, IsActive = true
    };

    /// <summary>DB・SMTPへ接続せずに受付処理だけを動かすサービスを作成します。</summary>
    /// <param name="db">検証用のContextです。</param>
    /// <param name="clock">実時刻の代替です。</param>
    /// <param name="allocator">採番日を記録する代替です。</param>
    /// <returns>検証用の問い合わせ受付サービスです。</returns>
    private static InquiryService CreateService(PortalDbContext db, IApplicationClock clock, IInquiryIdAllocator allocator) =>
        new(db, new StubCodeMaster(), new RejectStorage(), new RejectPolicies(), new AcceptingMailSender(),
            new StubTemplateRenderer(), clock, allocator, new SkippingActivityLogger());

    /// <summary>採番へ渡された日付を記録する代替です。</summary>
    private sealed class RecordingAllocator : IInquiryIdAllocator
    {
        /// <summary>採番を求められた日付です。</summary>
        public DateOnly? Requested { get; private set; }

        /// <summary>日付を記録し、その日の1件目の番号を返します。</summary>
        public Task<string> AllocateAsync(DateOnly date, CancellationToken ct = default)
        {
            Requested = date;
            return Task.FromResult(date.ToString("yyyyMMdd") + "0001");
        }
    }

    /// <summary>固定のUTC時刻を返す代替です。</summary>
    /// <param name="utcNow">返すUTC時刻です。</param>
    private sealed class StubClock(DateTimeOffset utcNow) : IApplicationClock
    {
        /// <summary>固定のUTC時刻を返します。</summary>
        public DateTimeOffset GetUtcNow() => utcNow;

        /// <summary>JSTへ変換します。</summary>
        public DateTimeOffset ToJst(DateTimeOffset utc) => utc.ToOffset(TimeSpan.FromHours(9));
    }

    /// <summary>問い合わせカテゴリだけを返す代替です。</summary>
    private sealed class StubCodeMaster : ICodeMasterReader
    {
        /// <summary>選択肢は受付処理で使用しないため空で返します。</summary>
        public Task<IReadOnlyList<CodeOption>> GetOptionsAsync(string codeType, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CodeOption>>([]);

        /// <summary>登録済みの質問カテゴリとして返します。</summary>
        public Task<CodeOption?> FindAsync(string codeType, string codeValue, CancellationToken ct = default) =>
            Task.FromResult<CodeOption?>(codeType == "INQUIRY_CATEGORY" && codeValue == "QUESTION" ? new("QUESTION", "質問", 10, null) : null);
    }

    /// <summary>添付のない検証で一時保存の利用を検出する代替です。</summary>
    private sealed class RejectStorage : IFileStorage
    {
        /// <summary>一時保存を呼び出さないことを確認します。</summary>
        public Task<TemporaryFileHandle> SaveTemporaryAsync(TemporaryFileRequest request, Stream source, CancellationToken ct = default) =>
            throw new InvalidOperationException("添付なしの検証で一時保存を使用しました。");

        /// <summary>永続保存は使用しません。</summary>
        public Task<StoredFile> SavePermanentAsync(PermanentFileRequest request, Stream source, CancellationToken ct = default) =>
            throw new InvalidOperationException();

        /// <summary>読取りは使用しません。</summary>
        public Task<Stream> OpenReadAsync(IStoredFileReference reference, CancellationToken ct = default) => throw new InvalidOperationException();

        /// <summary>削除は使用しません。</summary>
        public Task<FileDeleteResult> DeleteAsync(IStoredFileReference reference, CancellationToken ct = default) => throw new InvalidOperationException();
    }

    /// <summary>添付のない検証でアップロード条件の取得を検出する代替です。</summary>
    private sealed class RejectPolicies : IUploadPolicyProvider
    {
        /// <summary>条件取得を呼び出さないことを確認します。</summary>
        public Task<UploadPolicySnapshot> GetAsync(UploadPurpose purpose, string? toolId = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("添付なしの検証でアップロード条件を取得しました。");
    }

    /// <summary>SMTPへ接続せず受付成功を返す代替です。</summary>
    private sealed class AcceptingMailSender : IMailSender
    {
        /// <summary>常に受付完了を返します。</summary>
        public Task<MailSendResult> SendAsync(MailRequest request, CancellationToken ct = default) =>
            Task.FromResult(new MailSendResult(DeliveryOutcome.Succeeded));
    }

    /// <summary>件名・本文の整形を伴わない代替です。</summary>
    private sealed class StubTemplateRenderer : IMailTemplateRenderer
    {
        /// <summary>固定の件名と本文を返します。</summary>
        public MailContent Render(string templateKey, IReadOnlyDictionary<string, string> model) => new("件名", "本文");
    }

    /// <summary>操作ログを記録しない代替です。</summary>
    private sealed class SkippingActivityLogger : IActivityLogger
    {
        /// <summary>記録を行わずに完了を返します。</summary>
        public Task<LogWriteResult> WriteAsync(ActivityEvent entry, CancellationToken ct = default) =>
            Task.FromResult(LogWriteResult.Skipped);
    }
}
