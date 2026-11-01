using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Common;
using Htn.Domain.Directory;
using Htn.Domain.Invitations;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Invitations;

/// <summary>
/// Phase 7B: invitations per event (frame 18-adjacent). Pick organizations from the Phase 7A directory, verify
/// their contact, merge into the event's letter template, and send — only to a currently-verified contact.
/// Board-only (<c>Invitations.Manage</c>): unlike <c>Directory.View</c>, there is no lead carve-out here.
/// </summary>
public static class InvitationsEndpoints
{
    public static RouteGroupBuilder MapInvitations(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/invitations").WithTags("Invitations")
            .RequireAuthorization(Policies.InvitationsManage);

        g.MapGet("/", List);
        g.MapGet("/template", GetTemplate);
        g.MapPut("/template", PutTemplate).RequireRateLimiting(SponsorsRateLimiting.Write).Validate<InvitationTemplateRequest>();
        g.MapPost("/", Create).RequireRateLimiting(SponsorsRateLimiting.Write).Validate<CreateInvitationsRequest>();
        g.MapPost("/{invitationId:guid}/preview", Preview).RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPost("/{invitationId:guid}/send", Send).RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPost("/{invitationId:guid}/status", AdvanceStatus)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<InvitationStatusRequest>();
        g.MapDelete("/{invitationId:guid}", Delete).RequireRateLimiting(SponsorsRateLimiting.Write);

        return g;
    }

    // ---------- List ----------

    private static async Task<IResult> List(
        Guid eventId, InvitationStatus? status, HtnDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var now = clock.GetUtcNow();

        var query = db.EventInvitations.AsNoTracking().Where(i => i.EventId == eventId);
        if (status is { } s) query = query.Where(i => i.Status == s);
        var invitations = await query.ToListAsync(ct);

        var orgIds = invitations.Select(i => i.DirectoryOrgId).Distinct().ToList();
        var orgs = await db.DirectoryOrgs.AsNoTracking().Where(o => orgIds.Contains(o.Id)).ToListAsync(ct);
        var orgById = orgs.ToDictionary(o => o.Id);
        var primaries = await db.DirectoryContacts.AsNoTracking()
            .Where(c => orgIds.Contains(c.OrgId) && c.VoidedAt == null && c.IsPrimary).ToListAsync(ct);
        var primaryByOrg = primaries.ToDictionary(c => c.OrgId);

        var rows = invitations
            .OrderBy(i => orgById.GetValueOrDefault(i.DirectoryOrgId)?.Name)
            .Select(i =>
            {
                var org = orgById.GetValueOrDefault(i.DirectoryOrgId);
                var verified = InvitationsOps.IsVerified(primaryByOrg.GetValueOrDefault(i.DirectoryOrgId), now);
                return new InvitationListItemDto(
                    i.Id, i.DirectoryOrgId, org?.Name ?? "(removed organization)", org?.Kind.ToString() ?? "",
                    i.Status, i.SentAt, i.LastStatusAt, verified);
            }).ToList();
        return TypedResults.Ok(rows);
    }

    // ---------- Template ----------

    private static async Task<IResult> GetTemplate(Guid eventId, HtnDbContext db, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var template = await db.EventInvitationTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.EventId == eventId, ct);
        return template is null
            ? TypedResults.Ok(new EventInvitationTemplateDto(eventId, "", "", DateTimeOffset.MinValue))
            : TypedResults.Ok(InvitationsViews.Template(template));
    }

    private static async Task<IResult> PutTemplate(
        Guid eventId, InvitationTemplateRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        var template = await db.EventInvitationTemplates.FirstOrDefaultAsync(t => t.EventId == eventId, ct);
        var before = template is null ? null : new { template.Subject, template.Body };
        if (template is null)
        {
            template = EventInvitationTemplate.Create(eventId, new NewEventInvitationTemplate(req.Subject!, req.Body!), me.Id!.Value, now);
            db.EventInvitationTemplates.Add(template);
        }
        else
        {
            template.Update(new NewEventInvitationTemplate(req.Subject!, req.Body!), me.Id!.Value, now);
        }
        audit.Record(me.Id, InvitationAuditActions.TemplateUpdate, nameof(EventInvitationTemplate), eventId.ToString(),
            before: before, after: new { template.Subject, template.Body });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(InvitationsViews.Template(template));
    }

    // ---------- Pick list ----------

    private static async Task<IResult> Create(
        Guid eventId, CreateInvitationsRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        var wanted = req.OrgIds!.Distinct().ToList();
        var existingOrgIds = await db.EventInvitations.AsNoTracking()
            .Where(i => i.EventId == eventId && wanted.Contains(i.DirectoryOrgId)).Select(i => i.DirectoryOrgId).ToListAsync(ct);

        var created = new List<Guid>();
        var skipped = new List<Guid>();
        foreach (var orgId in wanted)
        {
            if (existingOrgIds.Contains(orgId)) { skipped.Add(orgId); continue; }
            var invitation = EventInvitation.Create(eventId, orgId, me.Id!.Value, now);
            db.EventInvitations.Add(invitation);
            created.Add(orgId);
        }
        if (created.Count > 0)
            audit.Record(me.Id, InvitationAuditActions.Create, nameof(EventInvitation), eventId.ToString(), after: new { eventId, orgIds = created });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new CreateInvitationsResultDto(created, skipped));
    }

    private static async Task<IResult> Delete(
        Guid eventId, Guid invitationId, HtnDbContext db, IAuditLog audit, ICurrentUser me, CancellationToken ct)
    {
        var invitation = await db.EventInvitations.FirstOrDefaultAsync(i => i.Id == invitationId && i.EventId == eventId, ct);
        if (invitation is null) return TypedResults.NotFound();
        if (!invitation.IsNotSent)
            return InvitationsOps.Conflict("This invitation has already been sent and can no longer be removed.", InvitationErrorCodes.AlreadySent);

        db.EventInvitations.Remove(invitation);
        audit.Record(me.Id, InvitationAuditActions.Delete, nameof(EventInvitation), invitation.Id.ToString());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ---------- Preview / send ----------

    private static async Task<IResult> Preview(Guid eventId, Guid invitationId, HtnDbContext db, CancellationToken ct)
    {
        var invitation = await db.EventInvitations.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invitationId && i.EventId == eventId, ct);
        if (invitation is null) return TypedResults.NotFound();
        var merged = await MergedLetterAsync(db, invitation, ct);
        if (merged is null) return TypedResults.NotFound();
        return TypedResults.Ok(new InvitationPreviewDto(merged.Subject, merged.Body));
    }

    private static async Task<IResult> Send(
        Guid eventId, Guid invitationId, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var invitation = await db.EventInvitations.FirstOrDefaultAsync(i => i.Id == invitationId && i.EventId == eventId, ct);
        if (invitation is null) return TypedResults.NotFound();
        if (!invitation.IsNotSent)
            return InvitationsOps.Conflict("This invitation has already been sent.", InvitationErrorCodes.AlreadySent);

        var org = await db.DirectoryOrgs.AsNoTracking().FirstOrDefaultAsync(o => o.Id == invitation.DirectoryOrgId, ct);
        if (org is null) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        // Server-side re-check against live contact state — never trust a client-supplied "verified" flag.
        var contact = await InvitationsOps.PrimaryContactAsync(db, org.Id, ct);
        if (!InvitationsOps.IsVerified(contact, now))
            return InvitationsOps.Conflict("The organization's contact must be verified before sending.", InvitationErrorCodes.ContactNotVerified);

        var template = await db.EventInvitationTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.EventId == eventId, ct);
        if (template is null) return InvitationsOps.Conflict("Set up the invitation letter template first.", "no_template");
        var ev = await db.Events.AsNoTracking().FirstAsync(e => e.Id == eventId, ct);

        var addressee = new InvitationAddressee(contact!.HolderName, contact.Position, org.AddresseeSalutation);
        var merged = InvitationMerge.Merge(template.Subject, template.Body, addressee, ev.StartsAt);

        try
        {
            invitation.Send(contact.Id, contact.Email, contact.HolderName, merged.Subject, merged.Body, contactIsVerified: true, me.Id!.Value, now);
        }
        catch (DomainException ex)
        {
            return Problems.Field("status", ex.Message, "invalid_send");
        }
        db.InvitationEvents.Add(InvitationEvent.Create(invitation.Id, InvitationStatus.Sent, me.Id, now));
        db.Outbox.Add(OutboxProcessor.InvitationLetterEmail(invitation.Id, now));
        audit.Record(me.Id, InvitationAuditActions.Send, nameof(EventInvitation), invitation.Id.ToString(),
            after: new { contactId = contact.Id, subject = merged.Subject });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, invitation, org.Name, ct));
    }

    // ---------- Manual status ----------

    private static async Task<IResult> AdvanceStatus(
        Guid eventId, Guid invitationId, InvitationStatusRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var invitation = await db.EventInvitations.FirstOrDefaultAsync(i => i.Id == invitationId && i.EventId == eventId, ct);
        if (invitation is null) return TypedResults.NotFound();
        var org = await db.DirectoryOrgs.AsNoTracking().FirstOrDefaultAsync(o => o.Id == invitation.DirectoryOrgId, ct);
        if (org is null) return TypedResults.NotFound();

        var now = clock.GetUtcNow();
        var before = invitation.Status;
        try
        {
            invitation.AdvanceStatus(req.Status!.Value, now, req.Note);
        }
        catch (DomainException ex)
        {
            return InvitationsOps.Conflict(ex.Message, InvitationErrorCodes.InvalidTransition);
        }
        db.InvitationEvents.Add(InvitationEvent.Create(invitation.Id, invitation.Status, me.Id, now, req.Note));

        // A bounce re-flags the organization's contact: treat it as if never verified, so it immediately needs a
        // check again, regardless of the 90-day window.
        if (invitation.Status == InvitationStatus.Bounced && invitation.ContactId is { } contactId)
        {
            var contact = await db.DirectoryContacts.FirstOrDefaultAsync(c => c.Id == contactId, ct);
            if (contact is not null && !contact.IsVoided)
            {
                contact.MarkBounced(now);
                db.DirectoryContactHistory.Add(DirectoryContactHistory.Bounced(contact.OrgId, contact.Id, me.Id, now, req.Note));
            }
        }

        audit.Record(me.Id, InvitationAuditActions.StatusAdvance, nameof(EventInvitation), invitation.Id.ToString(),
            before: new { status = before.ToString() }, after: new { status = invitation.Status.ToString() }, reason: req.Note);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await DetailDtoAsync(db, invitation, org.Name, ct));
    }

    // ---------- Helpers ----------

    private static async Task<MergedLetter?> MergedLetterAsync(HtnDbContext db, EventInvitation invitation, CancellationToken ct)
    {
        var org = await db.DirectoryOrgs.AsNoTracking().FirstOrDefaultAsync(o => o.Id == invitation.DirectoryOrgId, ct);
        if (org is null) return null;
        var template = await db.EventInvitationTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.EventId == invitation.EventId, ct);
        if (template is null) return null;
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == invitation.EventId, ct);
        if (ev is null) return null;
        var contact = await InvitationsOps.PrimaryContactAsync(db, org.Id, ct);
        var addressee = new InvitationAddressee(contact?.HolderName ?? org.Name, contact?.Position ?? "", org.AddresseeSalutation);
        return InvitationMerge.Merge(template.Subject, template.Body, addressee, ev.StartsAt);
    }

    private static async Task<InvitationDetailDto> DetailDtoAsync(HtnDbContext db, EventInvitation invitation, string orgName, CancellationToken ct)
    {
        var history = await db.InvitationEvents.AsNoTracking().Where(e => e.InvitationId == invitation.Id).ToListAsync(ct);
        return InvitationsViews.Detail(invitation, orgName, history);
    }
}
