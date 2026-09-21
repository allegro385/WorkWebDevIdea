using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.ErrorHandling;
using SalesSupport.Common.FileStorage;
using SalesSupport.Common.Validation;

namespace SalesSupport.Common.Mail;

/// <summary>送信する1通分の指定です。宛先と添付は認可済みの情報から作成します。</summary>
public sealed record MailRequest(IReadOnlyList<string> To, IReadOnlyList<string> Cc, IReadOnlyList<string> Bcc,
    string Subject, string Body, IReadOnlyList<TemporaryFileHandle>? Attachments = null);

/// <summary>SMTPの受付結果です。ErrorIdがある場合は記録済みを意味します。</summary>
public sealed record MailSendResult(DeliveryOutcome Outcome, Guid? ErrorId = null, string? FailureReason = null);

/// <summary>SMTPの組立と送信だけを担当します。宛先選定と再送は呼出元の業務処理です。</summary>
public interface IMailSender
{
    /// <summary>1通を一度だけ送信し、成功・失敗・結果不明を返します。</summary>
    Task<MailSendResult> SendAsync(MailRequest request, CancellationToken ct = default);
}

/// <summary>1送信ごとにSMTP接続を生成・切断し、自動再送を行わない送信器です。</summary>
public sealed class MailSender(IOptions<MailOptions> mail, IOptions<CommonOptions> common, IFileStorage storage, IErrorHandler errors) : IMailSender
{
    /// <summary>送信前検証・開発宛先置換・添付の破棄までを一度の呼出しで完結させます。</summary>
    public async Task<MailSendResult> SendAsync(MailRequest request, CancellationToken ct = default)
    {
        var settings = mail.Value;
        if (!settings.Enabled) throw new ConfigurationException("SalesSupport:Mail:Enabled");
        if (!string.IsNullOrEmpty(settings.UserName) && string.IsNullOrEmpty(settings.Password)) throw new ConfigurationException("SalesSupport:Mail:Password");
        if (HasLineBreak(request.Subject) || string.IsNullOrWhiteSpace(request.Subject)) return await FailAsync("検証", "INVALID_INPUT", ct);
        var recipients = ResolveRecipients(request, settings);
        if (recipients is null) return await FailAsync("検証", "INVALID_INPUT", ct);
        if (recipients.Value.Total == 0) return await FailAsync("宛先", "NO_RECIPIENTS", ct);
        if (settings.MaxRecipients is { } max && recipients.Value.Total > max) return await FailAsync("宛先", "INVALID_INPUT", ct);

        var streams = new List<Stream>();
        try
        {
            var message = await BuildAsync(request, recipients.Value, settings, streams, ct);
            return await TransmitAsync(message, settings, ct);
        }
        catch (ArgumentException) { return await FailAsync("添付", "INVALID_INPUT", ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return await FailAsync("組立", "CANCELLED", ct: default); }
        catch (Exception exception) { return await FailAsync("組立", "SEND_FAILED", ct: default, exception); }
        finally { foreach (var stream in streams) await stream.DisposeAsync(); }
    }

    /// <summary>接続から切断までを1送信で完結させ、結果不明を成功扱いにしません。</summary>
    private async Task<MailSendResult> TransmitAsync(MimeMessage message, MailOptions settings, CancellationToken ct)
    {
        var sending = false;
        using var client = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
        try
        {
            var security = settings.TlsMode == MailTlsMode.SslOnConnect ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(settings.Host, settings.Port, security, ct);
            if (!string.IsNullOrEmpty(settings.UserName)) await client.AuthenticateAsync(settings.UserName, settings.Password!, ct);
            sending = true;
            await client.SendAsync(message, ct);
            sending = false;
            // 受付完了後の切断失敗で送信結果を変更しません。
            try { await client.DisconnectAsync(true, CancellationToken.None); } catch (Exception) { }
            return new(DeliveryOutcome.Succeeded);
        }
        catch (SmtpCommandException exception)
        {
            // サーバーが明示的に拒否した場合は一部拒否を含めて失敗として扱います。
            return await FailAsync("送信", "SEND_FAILED", ct: default, exception);
        }
        catch (Exception exception) when (sending)
        {
            return await FailAsync("送信", "SEND_UNKNOWN", ct: default, exception, DeliveryOutcome.Unknown);
        }
        catch (OperationCanceledException) { return await FailAsync("接続", "CANCELLED", ct: default); }
        catch (Exception exception) { return await FailAsync("接続", "SEND_FAILED", ct: default, exception); }
    }

    /// <summary>添付Streamを送信完了まで保持し、元ファイル名を表示名として設定します。</summary>
    private async Task<MimeMessage> BuildAsync(MailRequest request, Recipients recipients, MailOptions settings, List<Stream> streams, CancellationToken ct)
    {
        var message = new MimeMessage { Subject = request.Subject };
        message.From.Add(MailboxAddress.Parse(settings.From));
        if (!string.IsNullOrEmpty(settings.ReplyTo)) message.ReplyTo.Add(MailboxAddress.Parse(settings.ReplyTo));
        foreach (var address in recipients.To) message.To.Add(MailboxAddress.Parse(address));
        foreach (var address in recipients.Cc) message.Cc.Add(MailboxAddress.Parse(address));
        foreach (var address in recipients.Bcc) message.Bcc.Add(MailboxAddress.Parse(address));
        var body = new BodyBuilder { TextBody = request.Body };
        foreach (var attachment in request.Attachments ?? [])
        {
            var stream = await storage.OpenReadAsync(attachment, ct);
            streams.Add(stream);
            body.Attachments.Add(attachment.OriginalName, stream, new ContentType("application", "octet-stream"));
        }
        message.Body = body.ToMessageBody();
        return message;
    }

    /// <summary>開発環境では実宛先を除去し、重複と形式不正を送信前に除きます。</summary>
    private Recipients? ResolveRecipients(MailRequest request, MailOptions settings)
    {
        if (!CommonValidation.IsEmail(settings.From) || settings.ReplyTo is { Length: > 0 } && !CommonValidation.IsEmail(settings.ReplyTo)) return null;
        if (common.Value.IsDevelopment)
        {
            if (!CommonValidation.IsEmail(settings.DevelopmentRecipient)) return null;
            return new Recipients([settings.DevelopmentRecipient!], [], [], 1);
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> to = [], cc = [], bcc = [];
        foreach (var (source, target) in new[] { (request.To, to), (request.Cc, cc), (request.Bcc, bcc) })
            foreach (var address in source ?? [])
            {
                if (!CommonValidation.IsEmail(address)) return null;
                if (seen.Add(address)) target.Add(address);
            }
        return new Recipients(to, cc, bcc, seen.Count);
    }

    /// <summary>失敗を一度だけ記録し、記録済みのErrorIdを呼出元へ返します。</summary>
    private async Task<MailSendResult> FailAsync(string stage, string reason, CancellationToken ct, Exception? exception = null, DeliveryOutcome outcome = DeliveryOutcome.Failed)
    {
        var details = new Dictionary<string, string> { ["処理段階"] = stage, ["理由"] = reason };
        var error = await errors.HandleAsync(new ErrorRequest(Exception: exception, StatusCode: 503, Details: details), ct);
        return new(outcome, error.ErrorId, reason);
    }

    /// <summary>件名・アドレスへのヘッダー注入になる改行を検出します。</summary>
    private static bool HasLineBreak(string value) => value.Any(x => x is '\r' or '\n');

    /// <summary>送信前に確定した宛先の組です。</summary>
    private readonly record struct Recipients(IReadOnlyList<string> To, IReadOnlyList<string> Cc, IReadOnlyList<string> Bcc, int Total);
}
