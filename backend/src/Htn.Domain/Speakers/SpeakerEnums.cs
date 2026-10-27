namespace Htn.Domain.Speakers;

/// <summary>A speaker's confirmation state, independent of the agenda. Invited until they confirm; declined with a reason.</summary>
public enum SpeakerStatus
{
    Invited,
    Confirmed,
    Declined,
}
