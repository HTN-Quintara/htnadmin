namespace Htn.Api.Features.Leads;

public static class LeadErrorCodes
{
    public const string InvalidTransition = "invalid_transition";
    public const string ReasonRequired = "reason_required";
    public const string LeadClosed = "lead_closed";
    public const string AlreadyLinked = "already_linked";
    public const string NotConfirmed = "not_confirmed";
    public const string DuplicateSpeakerEmail = "duplicate_speaker_email";
}

public static class LeadAuditActions
{
    public const string Create = "lead.create";
    public const string Update = "lead.update";
    public const string Status = "lead.status";
    public const string Confirm = "lead.confirm";
    public const string SpeakerCreate = "speaker.create";
}
