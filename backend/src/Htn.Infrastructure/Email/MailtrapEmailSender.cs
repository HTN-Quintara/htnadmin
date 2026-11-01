using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Htn.Infrastructure.Email;

/// <summary>
/// Sends through the Mailtrap Email API. Retries (429/5xx) come from the resilience handler on the typed client. The
/// settings are resolved per send (saved in Settings, else environment); with no token anywhere it hands the message
/// to <see cref="LoggingEmailSender"/>.
/// </summary>
public sealed partial class MailtrapEmailSender(
    HttpClient http, IMailtrapConfigProvider config, LoggingEmailSender fallback, ILogger<MailtrapEmailSender> logger) : IEmailSender
{
    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var effective = await config.GetAsync(ct);
        var o = effective.Options;
        if (!o.IsConfigured) return await fallback.SendAsync(message, ct);
        var from = o.FromAddress();
        var replyTo = string.IsNullOrWhiteSpace(o.ReplyTo) ? null : new System.Net.Mail.MailAddress(o.ReplyTo);
        var body = new MailtrapRequest(
            new MailtrapAddress(from.Address, string.IsNullOrEmpty(from.DisplayName) ? null : from.DisplayName),
            [new MailtrapAddress(message.To, message.ToName)],
            message.Subject, message.Html, message.Text, message.Category,
            message.AllAttachments.Count == 0 ? null : message.AllAttachments.Select(a => new MailtrapAttachment(
                Convert.ToBase64String(a.Content), a.FileName, a.ContentType,
                a.IsInline ? "inline" : "attachment", a.ContentId)).ToArray(),
            replyTo is null ? null : new MailtrapAddress(replyTo.Address, string.IsNullOrEmpty(replyTo.DisplayName) ? null : replyTo.DisplayName));

        using var request = new HttpRequestMessage(HttpMethod.Post, o.SendUri()) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiToken);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Mailtrap returned {(int)response.StatusCode}: {ErrorSummary(text)}", null, response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<MailtrapResponse>(ct);
        var id = result?.MessageIds is { Length: > 0 } ids ? ids[0] : null;
        logger.LogInformation("Mailtrap accepted {Category} email, message id {MessageId}", message.Category, id);
        return new EmailSendResult(id);
    }

    public const int ErrorSummaryMaxLength = 200;

    /// <summary>
    /// The first error from a Mailtrap error body (<c>{"errors":[...]}</c> or <c>{"error":"..."}</c>), shortened, with
    /// email addresses masked. A body that isn't Mailtrap JSON is not echoed (it could be an HTML page or carry data).
    /// </summary>
    public static string ErrorSummary(string? body)
    {
        string? first = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("errors", out var errors))
                    first = errors.ValueKind switch
                    {
                        JsonValueKind.Array when errors.GetArrayLength() > 0 => errors[0].ToString(),
                        JsonValueKind.String => errors.GetString(),
                        JsonValueKind.Object => errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}").FirstOrDefault(),
                        _ => null,
                    };
                else if (doc.RootElement.TryGetProperty("error", out var error))
                    first = error.ToString();
            }
        }
        catch (JsonException)
        {
            first = null;
        }
        if (string.IsNullOrWhiteSpace(first)) return "no error details";
        first = EmailPattern().Replace(first, m => Htn.Domain.Common.EmailMask.Mask(m.Value)).ReplaceLineEndings(" ");
        return first.Length > ErrorSummaryMaxLength ? first[..ErrorSummaryMaxLength] + "…" : first;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[^\s@""'<>,;:()]+@[^\s@""'<>,;:()]+")]
    private static partial System.Text.RegularExpressions.Regex EmailPattern();

    private sealed record MailtrapAddress(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name);

    private sealed record MailtrapRequest(
        [property: JsonPropertyName("from")] MailtrapAddress From,
        [property: JsonPropertyName("to")] MailtrapAddress[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("attachments"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MailtrapAttachment[]? Attachments,
        [property: JsonPropertyName("reply_to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MailtrapAddress? ReplyTo);

    /// <summary>Mailtrap attachment: base64 content; inline images also carry the content_id used as <c>cid:</c> in the HTML.</summary>
    private sealed record MailtrapAttachment(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("filename")] string FileName,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("disposition")] string Disposition,
        [property: JsonPropertyName("content_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContentId);

    private sealed record MailtrapResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("message_ids")] string[]? MessageIds);
}
