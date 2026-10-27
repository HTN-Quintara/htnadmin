namespace Htn.Domain.Directory;

/// <summary>What kind of organization a <see cref="DirectoryOrg"/> is.</summary>
public enum DirectoryOrgKind
{
    Government,
    Company,
    University,
    NonProfit,
}

/// <summary>What a <see cref="DirectoryContactHistory"/> row records.</summary>
public enum DirectoryContactHistoryKind
{
    /// <summary>The position holder changed; the row snapshots the prior position and holder name.</summary>
    HolderChanged,

    /// <summary>The contact was verified (reserved for future flows; "still correct" is the one endpoint uses today).</summary>
    Verified,

    /// <summary>Someone confirmed the contact is still correct, without a holder change.</summary>
    StillCorrect,

    /// <summary>An email to this contact bounced.</summary>
    Bounced,
}
