namespace Htn.Infrastructure.Email;

/// <summary>Outbox message types handled by the email dispatcher.</summary>
public static class EmailTypes
{
    public const string Invite = "email.invite";
    public const string PasswordReset = "email.password_reset";

    /// <summary>Someone was @mentioned in a task comment (frame 08). Queued only for people who can see that board.</summary>
    public const string TaskMention = "email.task_mention";

    /// <summary>The CFO approved or rejected an expense request (frame 26). Goes to the board member who filed it.</summary>
    public const string ExpenseRequestDecision = "email.expense_request_decision";

    /// <summary>The CFO saved a new version of an event budget (frame 25). One message per other board member.</summary>
    public const string BudgetSaved = "email.budget_saved";

    /// <summary>The initial speaker confirmation link, or a resend (frame 22/23). The raw token travels in Secret.</summary>
    public const string SpeakerLink = "email.speaker_link";

    /// <summary>The 6-digit email OTP for a speaker confirmation link (frame 23).</summary>
    public const string SpeakerOtp = "email.speaker_otp";

    /// <summary>A speaker's plus-one 50%-off registration voucher (frame 23).</summary>
    public const string PlusOneVoucher = "email.plus_one_voucher";

    /// <summary>An invitation letter, sent to an organization's verified contact (Phase 7B, frame 18-adjacent).</summary>
    public const string InvitationLetter = "email.invitation_letter";

    /// <summary>A test from Settings → Email delivery, proving the saved Mailtrap settings work.</summary>
    public const string DeliveryTest = "email.delivery_test";
}

/// <summary>Payload stored in the outbox. The raw token travels separately in <c>OutboxMessage.Secret</c>.</summary>
public sealed record InviteEmailPayload(Guid UserId, string To, string FirstName, string? InviterName, string Level, string? Position);

public sealed record PasswordResetEmailPayload(Guid UserId, string To, string FirstName);

/// <summary>A delivery test to any address an admin types; only the requester's first name goes in the body.</summary>
public sealed record DeliveryTestEmailPayload(string To, string RequestedBy);

/// <summary>
/// One @mention in one task comment. The recipient was already checked against the board's visibility when the comment
/// was posted; the renderer re-checks that the person still has an active login and that the comment still stands.
/// </summary>
public sealed record TaskMentionEmailPayload(Guid CommentId, Guid TaskId, Guid PersonId, string To, string FirstName);

/// <summary>
/// The decision on one expense request. Only the id travels: the renderer reads the decision, the note and the amount
/// from the current state, so a message queued before a correction never sends stale figures.
/// </summary>
public sealed record ExpenseRequestDecisionEmailPayload(Guid RequestId);

/// <summary>One board member's copy of "the CFO saved a budget version".</summary>
public sealed record BudgetSavedEmailPayload(Guid EventId, Guid VersionId, Guid PersonId);

/// <summary>Only the link id travels: the renderer re-reads current state (revoked? still the live link?) before sending.</summary>
public sealed record SpeakerLinkEmailPayload(Guid LinkId);

public sealed record SpeakerOtpEmailPayload(Guid LinkId);

public sealed record PlusOneVoucherEmailPayload(Guid PlusOneId);

/// <summary>Only the invitation id travels: the renderer re-reads it and builds the email from the frozen snapshot fields.</summary>
public sealed record InvitationLetterEmailPayload(Guid InvitationId);

public static class EmailText
{
    public static string WithArticle(string word) =>
        (word.Length > 0 && "AEIOUaeiou".Contains(word[0]) ? "an " : "a ") + word;
}

public static class Brand
{
    public const string OrgName = "HackTheNorth.PH";
    public const string Color = "#B71C1C";
}
