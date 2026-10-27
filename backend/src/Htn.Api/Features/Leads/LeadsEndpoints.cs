using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Sponsors;
using Htn.Domain.Common;
using Htn.Domain.Leads;
using Htn.Domain.Speakers;
using Htn.Domain.Sponsors;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Leads;

/// <summary>
/// Frame 17: the sponsor and speaker leads kanban, board-only (<c>Leads.Manage</c>). One table, a kind discriminator.
/// Confirming a speaker lead creates the speaker it became (so a confirmation link can be sent); confirming a sponsor
/// lead with a cash ask creates the sponsor entry, otherwise it just confirms and the board opens the sponsors screen.
/// </summary>
public static class LeadsEndpoints
{
    public static RouteGroupBuilder MapLeads(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/leads").WithTags("Leads");
        g.MapGet("/", Board).RequireAuthorization(Policies.LeadsManage);
        g.MapPost("/", Create).RequireAuthorization(Policies.LeadsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<LeadRequest>();
        g.MapPut("/{id:guid}", Update).RequireAuthorization(Policies.LeadsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<LeadRequest>();
        g.MapPost("/{id:guid}/status", SetStatus).RequireAuthorization(Policies.LeadsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<LeadStatusRequest>();
        g.MapPost("/{id:guid}/confirm", Confirm).RequireAuthorization(Policies.LeadsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        return g;
    }

    /// <summary>Minimal speaker endpoints that live with the leads slice because a confirmed speaker lead creates one.</summary>
    public static RouteGroupBuilder MapSpeakersBasics(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/speakers").WithTags("Speakers");
        // Read is open to anyone who can see the event (the agenda uses it to pick speakers).
        g.MapGet("/", ListSpeakers).RequireAuthorization(Policies.EventsView);
        // Direct creation is board-only for now; 6D scopes Speakers.Manage to the Programs lead too.
        g.MapPost("/", CreateSpeaker).RequireAuthorization(Policies.SpeakersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<SpeakerCreateRequest>();
        return g;
    }

    private static async Task<IResult> Board(
        Guid eventId, LeadKind? kind, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var k = kind ?? LeadKind.Sponsor;
        var today = ManilaTime.ManilaDate(clock.GetUtcNow());

        var leads = await db.Leads.AsNoTracking()
            .Where(l => l.EventId == eventId && l.Kind == k)
            .OrderBy(l => l.NextStepDue ?? DateOnly.MaxValue).ThenByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        var tierNames = await db.SponsorTiers.AsNoTracking().Where(t => t.EventId == eventId)
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var ownerNames = await OwnerNamesAsync(db, leads.Select(l => l.OwnerPersonId), ct);

        var cards = leads.Select(l => LeadsViews.Card(l, today,
            l.AskTierId is { } tid ? tierNames.GetValueOrDefault(tid) : null,
            l.OwnerPersonId is { } pid ? ownerNames.GetValueOrDefault(pid) : null)).ToList();

        return TypedResults.Ok(new LeadsBoardDto(k, Stats(k, cards), LeadsViews.Columns(cards)));
    }

    private static async Task<IResult> Create(
        Guid eventId, LeadRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        if (req.AskTierId is { } tid && tid != Guid.Empty && !await db.SponsorTiers.AnyAsync(t => t.Id == tid && t.EventId == eventId, ct))
            return Problems.Field("askTierId", "That tier no longer exists.", SponsorErrorCodes.UnknownTier);

        var now = clock.GetUtcNow();
        var lead = Lead.Create(eventId, ToNew(req), me.Id!.Value, now);
        db.Leads.Add(lead);
        audit.Record(me.Id, LeadAuditActions.Create, nameof(Lead), lead.Id.ToString(),
            after: new { lead.Subject, kind = lead.Kind.ToString() });
        await db.SaveChangesAsync(ct);
        return await Board(eventId, lead.Kind, db, clock, ct);
    }

    private static async Task<IResult> Update(
        Guid eventId, Guid id, LeadRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id && l.EventId == eventId, ct);
        if (lead is null) return TypedResults.NotFound();
        try { lead.Update(ToNew(req), me.Id!.Value, clock.GetUtcNow()); }
        catch (DomainException ex) { return Problems.Field("subject", ex.Message, LeadErrorCodes.LeadClosed); }
        audit.Record(me.Id, LeadAuditActions.Update, nameof(Lead), lead.Id.ToString(), after: new { lead.Subject });
        await db.SaveChangesAsync(ct);
        return await Board(eventId, lead.Kind, db, clock, ct);
    }

    private static async Task<IResult> SetStatus(
        Guid eventId, Guid id, LeadStatusRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id && l.EventId == eventId, ct);
        if (lead is null) return TypedResults.NotFound();
        var before = lead.Status.ToString();
        try { lead.MoveTo(req.Status!.Value, req.Reason, me.Id!.Value, clock.GetUtcNow()); }
        catch (DomainException ex) { return SponsorsOpsConflict(ex.Message); }
        audit.Record(me.Id, LeadAuditActions.Status, nameof(Lead), lead.Id.ToString(),
            new { status = before }, new { status = lead.Status.ToString() }, req.Reason);
        await db.SaveChangesAsync(ct);
        return await Board(eventId, lead.Kind, db, clock, ct);
    }

    private static async Task<IResult> Confirm(
        Guid eventId, Guid id, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id && l.EventId == eventId, ct);
        if (lead is null) return TypedResults.NotFound();
        if (lead.IsLinked)
            return TypedResults.Ok(new ConfirmLeadResultDto(lead.Id, lead.SponsorId, lead.SpeakerId));

        var now = clock.GetUtcNow();
        Guid? sponsorId = null, speakerId = null;

        if (lead.Kind == LeadKind.Speaker)
        {
            if (string.IsNullOrWhiteSpace(lead.ContactEmail))
                return Problems.Field("contactEmail", "Add the speaker's email before confirming, so the link can be sent.", LeadErrorCodes.ReasonRequired);
            var speakerEmail = lead.ContactEmail!.Trim().ToLowerInvariant();
            if (await db.Speakers.AnyAsync(sp => sp.EventId == eventId && sp.Email == speakerEmail && sp.VoidedAt == null, ct))
                return SponsorsOpsConflict("A speaker with that email already exists for this event.");
            var speaker = Speaker.FromLead(eventId, lead.Id, lead.Subject, lead.ContactEmail!, lead.ContactRole, me.Id!.Value, now);
            db.Speakers.Add(speaker);
            lead.LinkSpeaker(speaker.Id, me.Id!.Value, now);
            speakerId = speaker.Id;
            audit.Record(me.Id, LeadAuditActions.SpeakerCreate, nameof(Speaker), speaker.Id.ToString(),
                after: new { speaker.PersonName, speaker.Email, leadId = lead.Id });
        }
        else if (lead.AskAmount is { } amount)
        {
            // A sponsor lead with a cash ask becomes an AwaitingContract sponsor; the board finalizes it on the sponsors screen.
            var sponsor = Sponsor.Create(eventId, new NewSponsor(
                SponsorKind.Organization, lead.Subject, null, false, lead.ContactName, lead.ContactRole,
                lead.ContactEmail, lead.ContactMobile, lead.AskTierId, amount, null, null, 0, null), me.Id!.Value, now);
            if (await db.Sponsors.AnyAsync(sp => sp.EventId == eventId && sp.NameKey == sponsor.NameKey && sp.VoidedAt == null, ct))
                return SponsorsOpsConflict("A sponsor with that name already exists. Open the sponsors screen to link it.");
            db.Sponsors.Add(sponsor);
            lead.LinkSponsor(sponsor.Id, me.Id!.Value, now);
            sponsorId = sponsor.Id;
            audit.Record(me.Id, SponsorAuditActions.SponsorCreate, nameof(Sponsor), sponsor.Id.ToString(),
                after: new { sponsor.Name, fromLead = lead.Id });
        }
        else
        {
            // No cash ask: just confirm; the board creates the sponsor entry on the sponsors screen.
            lead.MoveTo(LeadStatus.Confirmed, null, me.Id!.Value, now);
        }

        audit.Record(me.Id, LeadAuditActions.Confirm, nameof(Lead), lead.Id.ToString(),
            after: new { sponsorId, speakerId });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new ConfirmLeadResultDto(lead.Id, sponsorId, speakerId));
    }

    private static async Task<IResult> ListSpeakers(Guid eventId, HtnDbContext db, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var speakers = await db.Speakers.AsNoTracking()
            .Where(sp => sp.EventId == eventId && sp.VoidedAt == null)
            .OrderBy(sp => sp.PersonName)
            .Select(sp => new SpeakerSummaryDto(sp.Id, sp.PersonName, sp.Status.ToString()))
            .ToListAsync(ct);
        return TypedResults.Ok(speakers);
    }

    private static async Task<IResult> CreateSpeaker(
        Guid eventId, SpeakerCreateRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var email = req.Email!.Trim().ToLowerInvariant();
        if (await db.Speakers.AnyAsync(sp => sp.EventId == eventId && sp.Email == email && sp.VoidedAt == null, ct))
            return SponsorsOpsConflict("A speaker with that email already exists for this event.");
        var now = clock.GetUtcNow();
        var speaker = Speaker.Create(eventId, req.Name!, req.Email!, req.Affiliation, me.Id!.Value, now);
        db.Speakers.Add(speaker);
        audit.Record(me.Id, LeadAuditActions.SpeakerCreate, nameof(Speaker), speaker.Id.ToString(),
            after: new { speaker.PersonName, speaker.Email });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new SpeakerSummaryDto(speaker.Id, speaker.PersonName, speaker.Status.ToString()));
    }

    // ---------- Helpers ----------

    private static NewLead ToNew(LeadRequest r) => new(
        r.Kind!.Value, r.Subject!, r.ContactName, r.ContactRole, r.ContactEmail, r.ContactMobile,
        r.AskTierId, r.AskAmount, r.AskNote, r.OwnerPersonId, r.NextStep, r.NextStepDue);

    private static IResult SponsorsOpsConflict(string detail) =>
        Problems.Result(StatusCodes.Status409Conflict, "That cannot be done right now.", detail, LeadErrorCodes.InvalidTransition);

    private static async Task<Dictionary<Guid, string>> OwnerNamesAsync(HtnDbContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var set = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (set.Count == 0) return [];
        return await db.People.AsNoTracking().Where(p => set.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => (p.FirstName + " " + p.LastName).Trim(), ct);
    }

    private static List<LeadStatDto> Stats(LeadKind kind, List<LeadCardDto> cards)
    {
        var open = cards.Where(c => c.Status is not (LeadStatus.Declined)).ToList();
        var overdue = cards.Count(c => c.Overdue);
        if (kind == LeadKind.Sponsor)
        {
            var pipeline = open.Where(c => c.Status is not LeadStatus.Confirmed).Sum(c => c.AskAmount ?? 0m);
            var confirmed = cards.Where(c => c.Status == LeadStatus.Confirmed).Sum(c => c.AskAmount ?? 0m);
            return
            [
                new("In pipeline", LeadsViews.Peso(pipeline)),
                new("Confirmed", LeadsViews.Peso(confirmed)),
                new("Overdue follow-ups", overdue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ];
        }
        return
        [
            new("Leads", cards.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Confirmed", cards.Count(c => c.Status == LeadStatus.Confirmed).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Overdue follow-ups", overdue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ];
    }
}
