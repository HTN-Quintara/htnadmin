using Htn.Domain.Organization;

namespace Htn.Domain.Merch;

/// <summary>
/// Per-event merch discount percentages by buyer level — the same "one settings row per event, lazily created with
/// defaults" shape as <see cref="Htn.Domain.Sponsors.EventPartnerSettings"/>. The defaults come from
/// <c>OrgSettings</c>' <c>*MerchDiscountPct</c> columns (Goon 20 / Apprentice 15 / Member 10 / Board 0), passed in by
/// the caller at the moment the row is first read — this entity itself doesn't depend on <c>OrgSettings</c>.
/// Attendees (no <see cref="PersonLevel"/> resolved) always pay full price; see <see cref="PctFor"/>.
/// </summary>
public sealed class EventMerchSettings
{
    private EventMerchSettings() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public int GoonPct { get; private set; }
    public int ApprenticePct { get; private set; }
    public int MemberPct { get; private set; }
    public int BoardPct { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }

    public static EventMerchSettings Default(
        Guid eventId, int goonPct, int apprenticePct, int memberPct, int boardPct, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(at),
        EventId = eventId,
        GoonPct = MerchRules.Pct(goonPct, "Goon discount"),
        ApprenticePct = MerchRules.Pct(apprenticePct, "Apprentice discount"),
        MemberPct = MerchRules.Pct(memberPct, "Member discount"),
        BoardPct = MerchRules.Pct(boardPct, "Board discount"),
        CreatedAt = at,
        UpdatedAt = at,
    };

    public void Update(int goonPct, int apprenticePct, int memberPct, int boardPct, Guid actorId, DateTimeOffset at)
    {
        GoonPct = MerchRules.Pct(goonPct, "Goon discount");
        ApprenticePct = MerchRules.Pct(apprenticePct, "Apprentice discount");
        MemberPct = MerchRules.Pct(memberPct, "Member discount");
        BoardPct = MerchRules.Pct(boardPct, "Board discount");
        UpdatedAt = at;
        UpdatedById = actorId;
    }

    /// <summary>The discount for a buyer at this level; null (unresolved) or an Attendee always pays full price.</summary>
    public int PctFor(PersonLevel? level) => level switch
    {
        PersonLevel.Goon => GoonPct,
        PersonLevel.Apprentice => ApprenticePct,
        PersonLevel.Member => MemberPct,
        PersonLevel.Board => BoardPct,
        _ => 0,
    };
}
