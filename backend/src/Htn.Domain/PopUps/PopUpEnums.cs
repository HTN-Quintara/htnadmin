namespace Htn.Domain.PopUps;

/// <summary>
/// How a pop-up's discount table is sourced (Phase 8C decision #4): <see cref="SameAsDefault"/> reads the live
/// <c>OrgSettings</c> merch-discount defaults (not any one event's, since leftover stock spans events),
/// <see cref="None"/> is always 0% and <see cref="Custom"/> uses the pop-up's own four <c>*Pct</c> columns.
/// </summary>
public enum PopUpDiscountMode
{
    SameAsDefault,
    None,
    Custom,
}

/// <summary>
/// A pop-up's lifecycle (Phase 8C decision #5): explicit and forward-only, <c>Planned → Open → NeedsCount →</c>
/// <see cref="ClosedBalanced"/> or <see cref="ClosedMissing"/>. There is no time-derived auto-transition and no
/// backward edge, ever (mirrors <c>Htn.Domain.Suppliers.EventSupplier</c>'s hire-status precedent, not
/// <c>EventInvitation</c>'s one Bounced exception). A CFO write-off on <see cref="ClosedMissing"/> posts an expense
/// but never changes the status again.
/// </summary>
public enum PopUpStatus
{
    Planned,
    Open,
    NeedsCount,
    ClosedBalanced,
    ClosedMissing,
}
