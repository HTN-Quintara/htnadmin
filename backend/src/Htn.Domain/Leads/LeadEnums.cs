namespace Htn.Domain.Leads;

/// <summary>A lead is either a sponsor prospect or a speaker prospect; the two share one pipeline and one screen.</summary>
public enum LeadKind
{
    Sponsor,
    Speaker,
}

/// <summary>The outreach pipeline: Prospect → Contacted → In talks → Confirmed / Declined.</summary>
public enum LeadStatus
{
    Prospect,
    Contacted,
    InTalks,
    Confirmed,
    Declined,
}

/// <summary>Where the lead came from. Directory leads arrive in Phase 7; until then every lead is entered by hand.</summary>
public enum LeadSource
{
    Manual,
    Directory,
}
