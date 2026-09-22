using Microsoft.AspNetCore.Mvc;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.UI;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;

namespace SalesSupport.Portal.Web.Controllers;

/// <summary>P007 問い合わせ・ご意見です。受付とメール送信を一度だけ試行します。</summary>
[Route("inquiries")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InquiriesController(IInquiryService inquiries, ICurrentUserAccessor current, IConfirmationStore confirmations) : Controller
{
    /// <summary>送信IDを識別するための固定値です。</summary>
    private const string TicketPurpose = "INQUIRY_SUBMIT";

    /// <summary>受付完了画面へ問い合わせIDだけを引き継ぐためのキーです。本文・秘密情報は保持しません。</summary>
    private const string CompletedKey = "SalesSupport.Portal.InquiryId";

    /// <summary>入力画面を表示し、本人へ束縛した一回限りの送信IDを発行します。</summary>
    [HttpGet("new")]
    public Task<IActionResult> New(CancellationToken ct) => NewViewAsync(new InquiryInput(), null, ct);

    /// <summary>入力を検証して受け付けます。送信失敗時は受付済みの問い合わせIDを表示しません。</summary>
    /// <param name="input">カテゴリ・対象・内容と送信IDです。</param>
    /// <param name="attachment">任意の添付ファイル1件です。</param>
    /// <param name="ct">要求のキャンセルトークンです。</param>
    [HttpPost("new")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> New(InquiryInput input, IFormFile? attachment, CancellationToken ct)
    {
        if (current.User is not { } user) return Unauthorized();
        // 送信開始後の同じ送信IDでの再送は受け付けません。入力し直す場合は新しい送信IDを発行します。
        if (confirmations.Consume<OneTimeTicket>(input.SubmissionId, user.UserId) is not { Purpose: TicketPurpose })
            return await NewViewAsync(input, new OperationMessage(OperationMessageKind.Warning,
                "送信を受け付けられませんでした。内容を確認して、もう一度送信してください。"), ct);
        if (!ModelState.IsValid) return await NewViewAsync(input, null, ct);

        await using var content = attachment?.OpenReadStream();
        var submission = new InquirySubmission(user.UserId, input.CategoryCode, input.Target,
            input.Content, content, attachment?.FileName);
        var acceptance = await inquiries.SubmitAsync(submission, ct);
        switch (acceptance.Outcome)
        {
            case InquiryOutcome.Accepted when acceptance.InquiryId is { } inquiryId:
                TempData[CompletedKey] = inquiryId;
                return RedirectToAction(nameof(Completed));
            case InquiryOutcome.InvalidInput:
                ModelState.AddValidationResult(acceptance.Errors);
                return await NewViewAsync(input, null, ct);
            default:
                return await NewViewAsync(input, new OperationMessage(OperationMessageKind.Failure,
                    "受付できませんでした。改めて送信してください。"), ct);
        }
    }

    /// <summary>受付完了と問い合わせIDを再送信なしで表示します。</summary>
    [HttpGet("completed")]
    public IActionResult Completed()
    {
        if (TempData[CompletedKey] is not string inquiryId) return RedirectToAction(nameof(New));
        ViewData.SetPageShell(Shell("問い合わせ・ご意見"));
        return View(new InquiryCompletedViewModel(inquiryId));
    }

    /// <summary>入力画面を組み立てます。ファイル選択は復元せず、送信IDを新しく発行します。</summary>
    private async Task<IActionResult> NewViewAsync(InquiryInput input, OperationMessage? message, CancellationToken ct)
    {
        if (current.User is not { } user) return Unauthorized();
        var ticket = confirmations.Issue(user.UserId, new OneTimeTicket(TicketPurpose));
        if (ticket is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        input.SubmissionId = ticket.Value;

        ViewData.SetPageShell(Shell("問い合わせ・ご意見"));
        return View("New", new InquiryViewModel
        {
            Input = input,
            Categories = await inquiries.GetCategoriesAsync(ct),
            Targets = await inquiries.GetTargetsAsync(ct),
            AttachmentHint = await inquiries.GetAttachmentHintAsync(ct),
            Message = message
        });
    }

    /// <summary>問い合わせ画面の共通の表示情報を返します。</summary>
    private static PageShellModel Shell(string title) =>
        new() { PageTitle = title, Breadcrumbs = [new Breadcrumb("トップ", ""), new Breadcrumb(title)] };
}
