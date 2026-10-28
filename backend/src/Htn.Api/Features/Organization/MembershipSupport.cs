using Htn.Domain.Events;
using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Organization;

/// <summary>
/// Shared by <c>MerchEndpoints.Sell</c> (the POS membership tile, Phase 8B) and <c>PeopleEndpoints</c> (the People
/// page's "Create Member" dialog and person detail view) — the one place that resolves "which main event is the
/// current one" and "what should this membership's <c>ValidUntil</c> be", so both call sites agree.
/// </summary>
public static class MembershipSupport
{
    /// <summary>
    /// The main event membership dues are currently collected at: the one <c>OnSale</c> main event, if there is
    /// one, else the most recently started main event (so the People page can still show "paid at HTN Con 2026"
    /// context between events). Null if no main event has ever been configured.
    /// </summary>
    public static async Task<Event?> CurrentMainEventAsync(HtnDbContext db, CancellationToken ct)
    {
        var onSale = await db.Events.AsNoTracking()
            .Where(e => e.IsMainEvent && e.Status == EventStatus.OnSale)
            .OrderByDescending(e => e.StartsAt)
            .FirstOrDefaultAsync(ct);
        if (onSale is not null) return onSale;
        return await db.Events.AsNoTracking()
            .Where(e => e.IsMainEvent)
            .OrderByDescending(e => e.StartsAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// "Valid until the next main event" (SKILL.md): the start date of the main event for
    /// <paramref name="membershipYear"/> + 1, if one has been configured yet. Otherwise — a genuine ambiguity, since
    /// next year's main event is usually not scheduled when this year's dues are collected — one calendar year out
    /// from <paramref name="paidAt"/>, documented here as the deliberate fallback.
    /// </summary>
    public static async Task<DateTimeOffset> ResolveValidUntilAsync(
        HtnDbContext db, int membershipYear, DateTimeOffset paidAt, CancellationToken ct)
    {
        var next = await db.Events.AsNoTracking()
            .Where(e => e.IsMainEvent && e.MembershipYear == membershipYear + 1)
            .OrderBy(e => e.StartsAt)
            .FirstOrDefaultAsync(ct);
        return next?.StartsAt ?? paidAt.AddYears(1);
    }
}
