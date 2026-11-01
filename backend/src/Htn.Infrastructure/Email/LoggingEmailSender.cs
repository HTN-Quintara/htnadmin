using Htn.Domain.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Htn.Infrastructure.Email;

/// <summary>
/// Used when no Mailtrap token is configured (neither saved in Settings nor MAILTRAP_API_TOKEN). In Development it logs
/// the recipient and the plain-text body so invite/reset links can be followed locally. Elsewhere, with
/// Email:AllowLoggingSender=true, it logs the subject and a masked recipient, never the body (it holds single-use links).
/// Elsewhere without that flag it throws, so the outbox keeps the message and retries instead of dropping it.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment env, IConfiguration config) : IEmailSender
{
    public const string AllowLoggingSenderKey = "Email:AllowLoggingSender";

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        // Names and sizes only: attachment bytes (QR codes, logos) never go to the log.
        var attachments = message.AllAttachments.Count == 0
            ? "none"
            : string.Join(", ", message.AllAttachments.Select(a =>
                $"{a.FileName} ({a.ContentType}, {a.Content.Length} bytes{(a.IsInline ? $", inline cid:{a.ContentId}" : "")})"));
        if (env.IsDevelopment())
            logger.LogInformation("Email not sent (no Mailtrap token). To {To}, subject {Subject}, attachments: {Attachments}.\n{Text}",
                message.To, message.Subject, attachments, message.Text);
        else if (config.GetValue<bool>(AllowLoggingSenderKey))
            logger.LogWarning("Email not sent (no Mailtrap token). To {To}, subject {Subject}, {Count} attachment(s).",
                EmailMask.Mask(message.To), message.Subject, message.AllAttachments.Count);
        else
            throw new InvalidOperationException(
                "No Mailtrap API token is configured. Save one in Settings → Email delivery or set MAILTRAP_API_TOKEN.");
        return Task.FromResult(new EmailSendResult(null));
    }
}
