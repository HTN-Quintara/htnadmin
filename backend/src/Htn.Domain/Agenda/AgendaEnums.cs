namespace Htn.Domain.Agenda;

/// <summary>What kind of slot a session is. A Break (registration, lunch, awards) needs no speaker.</summary>
public enum SessionKind
{
    Keynote,
    Talk,
    Panel,
    Workshop,
    Break,
}

/// <summary>A speaker's part in a session. A panel has panellists and one moderator.</summary>
public enum SessionSpeakerRole
{
    Speaker,
    Moderator,
    Panellist,
}
