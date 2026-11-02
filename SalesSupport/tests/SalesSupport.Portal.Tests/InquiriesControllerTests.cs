using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.MasterData;
using SalesSupport.Portal.Web.Controllers;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>問い合わせ入力画面が、Serviceの項目エラーを入力欄へ表示できるキーで戻すことを確認します。</summary>
public sealed class InquiriesControllerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>入力項目のエラーは`Input.`付きキー、添付ファイルのエラーは添付欄のキーへ追加します。</summary>
    [Fact]
    public async Task InvalidInputDisplaysServiceErrorsAtInputFields()
    {
        var confirmations = new ConfirmationStore(new FixedClock());
        var submissionId = confirmations.Issue(UserId, new OneTimeTicket("INQUIRY_SUBMIT"))!.Value;
        var controller = new InquiriesController(new InvalidInquiryService(), new StubCurrentUserAccessor(), confirmations)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var response = await controller.New(new InquiryInput { SubmissionId = submissionId }, null, CancellationToken.None);

        Assert.IsType<ViewResult>(response);
        Assert.True(controller.ModelState.ContainsKey("Input.CategoryCode"));
        Assert.True(controller.ModelState.ContainsKey(InquiryService.AttachmentField));
    }

    /// <summary>受付時に入力項目と添付ファイルの項目エラーを返す代替です。</summary>
    private sealed class InvalidInquiryService : IInquiryService
    {
        /// <summary>選択肢のない状態を返します。</summary>
        public Task<IReadOnlyList<CodeOption>> GetCategoriesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CodeOption>>([]);

        /// <summary>選択肢のない状態を返します。</summary>
        public Task<IReadOnlyList<InquiryTargetOption>> GetTargetsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InquiryTargetOption>>([]);

        /// <summary>固定の案内文を返します。</summary>
        public Task<string> GetAttachmentHintAsync(CancellationToken ct = default) => Task.FromResult(".txt");

        /// <summary>カテゴリと添付ファイルの項目エラーを返します。</summary>
        public Task<InquiryAcceptance> SubmitAsync(InquirySubmission submission, CancellationToken ct = default) =>
            Task.FromResult(new InquiryAcceptance(InquiryOutcome.InvalidInput, null, new ValidationResult([
                new FieldError(nameof(InquiryInput.CategoryCode), "INVALID_INPUT", "カテゴリを選択してください。"),
                new FieldError(InquiryService.AttachmentField, "INVALID_FILE", "添付ファイルが不正です。")])));
    }

    /// <summary>検証済みの本人を返す代替です。</summary>
    private sealed class StubCurrentUserAccessor : ICurrentUserAccessor
    {
        /// <summary>固定の利用者です。</summary>
        public CurrentUser? User { get; } = new(UserId, "試験太郎", "A");
    }

    /// <summary>送信IDの期限判定に使う固定時刻です。</summary>
    private sealed class FixedClock : IApplicationClock
    {
        /// <summary>固定のUTC時刻を返します。</summary>
        public DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-09-21T00:00:00Z");

        /// <summary>日本標準時へ変換します。</summary>
        public DateTimeOffset ToJst(DateTimeOffset utc) => utc.ToOffset(TimeSpan.FromHours(9));
    }
}
