using Htn.Domain.Common;
using Htn.Domain.Events;

namespace Htn.Domain.Organization;

/// <summary>How a membership fee reached the ledger: at the event's POS, or typed in by an admin on the People page.</summary>
public enum MembershipPaidVia
{
    Pos,
    RecordedManually,
}

/// <summary>
/// One row per <c>(PersonId, MembershipYear)</c> — "membership is paid once per main-event year" (SKILL.md
/// "Levels and promotion"). <b>Fully immutable once written, with no update method at all</b>: a renewal for the
/// next main-event year is a brand new row, never an edit of this one — enforced again at the database by an
/// unconditional reject-all trigger (<c>Membership_Guards</c>), the same shape as <c>merch_sales_guard</c>, and by
/// <c>HtnDbContext.GuardAppendOnly()</c> in C#.
/// <para>
/// <see cref="ValidUntil"/> is resolved by the caller (<c>MembershipSupport</c> in <c>Htn.Api</c>, which can see the
/// database) from the next known main event's start date — "valid until the next main event" per the domain rule —
/// or, when there is no next main event on the calendar yet (a genuine ambiguity the business rules don't resolve),
/// one calendar year out from <see cref="PaidAt"/> as a sensible fallback. This entity only validates the value it
/// is handed; it never looks anything up itself.
/// </para>
/// </summary>
public sealed class Membership
{
    private Membership() { }

    public Guid Id { get; private set; }
    public Guid PersonId { get; private set; }
    public int MembershipYear { get; private set; }
    public decimal FeePaid { get; private set; }
    public DateTimeOffset PaidAt { get; private set; }
    public MembershipPaidVia PaidVia { get; private set; }

    /// <summary>The main event this was paid at, if paid via POS or explicitly tied to one manual record.</summary>
    public Guid? EventId { get; private set; }

    /// <summary>The admin or cashier who recorded the payment (or the "not paid yet"/"paid at event" roster entry).</summary>
    public Guid RecordedById { get; private set; }

    public DateTimeOffset ValidUntil { get; private set; }

    public static Membership Create(
        Guid personId, int membershipYear, decimal feePaid, DateTimeOffset paidAt, MembershipPaidVia paidVia,
        Guid? eventId, Guid recordedById, DateTimeOffset validUntil, DateTimeOffset at)
    {
        if (personId == Guid.Empty) throw new DomainException("A person is required.");
        if (membershipYear < Event.MinMembershipYear || membershipYear > Event.MaxMembershipYear)
            throw new DomainException("Unknown membership year.");
        if (feePaid < 0) throw new DomainException("The fee paid can't be negative.");
        if (!Enum.IsDefined(paidVia)) throw new DomainException("Unknown payment method.");
        if (recordedById == Guid.Empty) throw new DomainException("Who recorded this is required.");
        if (validUntil <= paidAt) throw new DomainException("Valid-until must be after the payment date.");

        return new Membership
        {
            Id = Guid.CreateVersion7(at),
            PersonId = personId,
            MembershipYear = membershipYear,
            FeePaid = decimal.Round(feePaid, 2),
            PaidAt = paidAt,
            PaidVia = paidVia,
            EventId = eventId,
            RecordedById = recordedById,
            ValidUntil = validUntil,
        };
    }
}
