using System.Net.Mail;
using Htn.Domain.Common;

namespace Htn.Domain.Settings;

public enum EmailDeliveryMode
{
    Sandbox,
    Live,
}

/// <summary>
/// Mailtrap delivery settings edited by the board (Settings.Manage), owned by <see cref="OrgSettings"/>. The API token is
/// stored only as a Data Protection ciphertext (<see cref="ProtectedApiToken"/>) plus its last four characters for display.
/// Environment variables stay the fallback while no token is saved here.
/// </summary>
public sealed class EmailDeliverySettings
{
    public const int EmailMaxLength = 254;
    public const int FromNameMaxLength = 100;
    public const int InboxIdMaxLength = 32;
    public const int TokenMinLength = 16;
    public const int TokenMaxLength = 256;
    public const int ProtectedTokenMaxLength = 2048;
    public const string DefaultFromEmail = "no-reply@hackthenorth.ph";
    public const string DefaultFromName = "HackTheNorth.PH";

    private EmailDeliverySettings() { }

    public EmailDeliveryMode Mode { get; private set; }
    public string? InboxId { get; private set; }
    public string FromEmail { get; private set; } = DefaultFromEmail;
    public string FromName { get; private set; } = DefaultFromName;
    public string? ReplyTo { get; private set; }
    public string? ProtectedApiToken { get; private set; }
    public string? TokenLast4 { get; private set; }

    /// <summary>Null until an admin first saves these settings; until then the environment values apply.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    public bool HasToken => !string.IsNullOrEmpty(ProtectedApiToken);
    public bool IsSaved => UpdatedAt is not null;

    public static EmailDeliverySettings Default() => new() { Mode = EmailDeliveryMode.Sandbox };

    public static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > EmailMaxLength) return false;
        try
        {
            var a = new MailAddress(value.Trim());
            return a.Address == value.Trim() && a.Host.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool IsValidInboxId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= InboxIdMaxLength && value.Trim().All(char.IsAsciiLetterOrDigit);

    public static bool IsValidToken(string? value) =>
        value is not null && value.Trim().Length is >= TokenMinLength and <= TokenMaxLength && !value.Trim().Any(char.IsWhiteSpace);

    internal void Update(EmailDeliveryMode mode, string? inboxId, string fromEmail, string fromName, string? replyTo, DateTimeOffset at)
    {
        if (!Enum.IsDefined(mode)) throw new DomainException("Choose sandbox or live.");
        inboxId = string.IsNullOrWhiteSpace(inboxId) ? null : inboxId.Trim();
        if (mode == EmailDeliveryMode.Sandbox && inboxId is null) throw new DomainException("Sandbox mode needs a Mailtrap inbox ID.");
        if (inboxId is not null && !IsValidInboxId(inboxId)) throw new DomainException("The inbox ID is letters and digits only.");
        if (!IsValidEmail(fromEmail)) throw new DomainException("Enter a valid from address.");
        fromName = fromName?.Trim() ?? "";
        if (fromName.Length is 0 or > FromNameMaxLength) throw new DomainException($"The from name is 1–{FromNameMaxLength} characters.");
        replyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo.Trim();
        if (replyTo is not null && !IsValidEmail(replyTo)) throw new DomainException("Enter a valid reply-to address.");

        Mode = mode;
        InboxId = inboxId;
        FromEmail = fromEmail.Trim();
        FromName = fromName;
        ReplyTo = replyTo;
        UpdatedAt = at;
    }

    internal void SetToken(string protectedToken, string last4, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(protectedToken) || protectedToken.Length > ProtectedTokenMaxLength)
            throw new DomainException("The protected token is missing or too long.");
        if (last4 is not { Length: 4 }) throw new DomainException("Keep exactly the last four characters of the token.");
        ProtectedApiToken = protectedToken;
        TokenLast4 = last4;
        UpdatedAt = at;
    }

    internal void ClearToken(DateTimeOffset at)
    {
        ProtectedApiToken = null;
        TokenLast4 = null;
        UpdatedAt = at;
    }

    /// <summary>The last four characters of a raw token, the only part ever shown or audited.</summary>
    public static string Last4(string rawToken) => rawToken.Trim()[^4..];
}
