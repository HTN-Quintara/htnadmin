namespace Htn.Domain.Sponsors;

/// <summary>A sponsor is an organization or a credited individual ("Anonymous" when they ask not to be named).</summary>
public enum SponsorKind
{
    Organization,
    Individual,
}

/// <summary>Sponsorship lifecycle: a signed contract moves it to <see cref="Confirmed"/>.</summary>
public enum SponsorStatus
{
    AwaitingContract,
    Confirmed,
}

/// <summary>The tier class shown on the sponsor cards. Partner and Individual are classes too, not only org tiers.</summary>
public enum TierClass
{
    Platinum,
    Gold,
    Silver,
    Partner,
    Individual,
}

/// <summary>How an organization is counted toward the partner threshold.</summary>
public enum PartnerCountBasis
{
    PaidOnly,
    AllRegistrants,
    CheckedIn,
}
