using Htn.Api.Common;
using Htn.Domain.Sponsors;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Sponsors;

/// <summary>The <c>code</c> extension on every sponsors/partners problem response, mirrored in the frontend.</summary>
public static class SponsorErrorCodes
{
    public const string AlreadyReceived = "already_received";
    public const string NotReceived = "not_received";
    public const string CashPosted = "cash_posted";
    public const string PeriodClosedUseFinance = "period_closed_use_finance";
    public const string NoCash = "no_cash";
    public const string NotConfirmed = "not_confirmed";
    public const string UnknownTier = "unknown_tier";
    public const string UnknownAccount = "unknown_account";
    public const string DuplicateName = "duplicate_name";
    public const string ThresholdNotReached = "threshold_not_reached";
    public const string AlreadyTagged = "already_tagged";
    public const string UnsupportedType = "unsupported_type";
}

/// <summary>Audit action strings, <c>&lt;entity&gt;.&lt;verb&gt;</c> like the rest of the system.</summary>
public static class SponsorAuditActions
{
    public const string TierSave = "sponsor_tier.save";
    public const string TierDeactivate = "sponsor_tier.deactivate";
    public const string SponsorCreate = "sponsor.create";
    public const string SponsorUpdate = "sponsor.update";
    public const string SponsorConfirm = "sponsor.confirm";
    public const string SponsorUnconfirm = "sponsor.unconfirm";
    public const string SponsorCashReceived = "sponsor.cash_received";
    public const string SponsorCashReversed = "sponsor.cash_reversed";
    public const string SponsorVoid = "sponsor.void";
    public const string SponsorLogo = "sponsor.logo";
    public const string PerkSave = "sponsor.perk_save";
    public const string PerkDeliver = "sponsor.perk_deliver";
    public const string PerkRemove = "sponsor.perk_remove";
    public const string PartnerSettings = "partner.settings";
    public const string PartnerTag = "partner.tag";
    public const string PartnerUntag = "partner.untag";
    public const string PartnerBrand = "partner.brand";
    public const string GroupLinkCreate = "partner.group_link_create";
    public const string GroupLinkRevoke = "partner.group_link_revoke";
}

/// <summary>One org's registrant counts, grouped by the normalized company key on attendees.</summary>
public sealed record PartnerGroupRow(string CompanyKey, string SampleName, int AllRegistrants, int Paid, int CheckedIn);

public static class SponsorsOps
{
    /// <summary>The event's partner settings, created with the defaults on first use.</summary>
    public static async Task<EventPartnerSettings> SettingsAsync(
        HtnDbContext db, Guid eventId, DateTimeOffset at, CancellationToken ct)
    {
        var local = db.EventPartnerSettings.Local.FirstOrDefault(s => s.EventId == eventId);
        if (local is not null) return local;
        var settings = await db.EventPartnerSettings.FirstOrDefaultAsync(s => s.EventId == eventId, ct);
        if (settings is not null) return settings;
        settings = EventPartnerSettings.Default(eventId, at);
        db.EventPartnerSettings.Add(settings);
        return settings;
    }

    /// <summary>
    /// Registrations grouped by the agency/company field (the generated <c>company_key</c>), the source of truth for
    /// partner status. One index scan on <c>ix_attendees_event_company_key</c> feeds the group-by.
    /// </summary>
    public static async Task<List<PartnerGroupRow>> CompanyGroupsAsync(
        HtnDbContext db, Guid eventId, CancellationToken ct) =>
        await db.Database.SqlQuery<PartnerGroupRow>($"""
            SELECT a.company_key AS company_key,
                   min(a.company) AS sample_name,
                   count(*)::int AS all_registrants,
                   count(*) FILTER (WHERE r.status IN ('Paid', 'CheckedIn'))::int AS paid,
                   count(*) FILTER (WHERE a.checked_in_at IS NOT NULL)::int AS checked_in
            FROM attendees a
            JOIN registrations r ON r.id = a.registration_id
            WHERE a.event_id = {eventId} AND a.company_key IS NOT NULL AND r.status <> 'Cancelled'
            GROUP BY a.company_key
            """).ToListAsync(ct);

    public static int ForBasis(PartnerGroupRow row, PartnerCountBasis basis) => basis switch
    {
        PartnerCountBasis.PaidOnly => row.Paid,
        PartnerCountBasis.CheckedIn => row.CheckedIn,
        _ => row.AllRegistrants,
    };

    public static IResult Conflict(string detail, string code) =>
        Problems.Result(StatusCodes.Status409Conflict, "That cannot be done right now.", detail, code);
}
