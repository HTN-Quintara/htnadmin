using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Committees;
using Htn.Domain.Common;
using Htn.Domain.Finance;
using Htn.Domain.Suppliers;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Suppliers;

/// <summary>
/// Phase 7C: a supplier hired for one event, tied to a budget category and an owning committee, with tracking-only
/// money and status (Quotation → Booked → DepositPaid → FullyPaid). No auto-post: cash actually leaving the
/// organization stays the CFO's manual ledger/expense-request flow. <c>Suppliers.Rate</c> is the net-new,
/// two-layer authorization shape of this slice — see the remarks on <see cref="Rate"/>.
/// </summary>
public static class EventSuppliersEndpoints
{
    public static RouteGroupBuilder MapEventSuppliers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/suppliers").WithTags("EventSuppliers");

        g.MapGet("/", List).RequireAuthorization(Policies.SuppliersView);
        g.MapPost("/", Create).RequireAuthorization(Policies.SuppliersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<NewHireRequest>();
        g.MapPatch("/{hireId:guid}", Update).RequireAuthorization(Policies.SuppliersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<UpdateHireRequest>();
        g.MapPost("/{hireId:guid}/void", Void).RequireAuthorization(Policies.SuppliersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();
        g.MapDelete("/{hireId:guid}", Delete).RequireAuthorization(Policies.SuppliersManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);

        g.MapGet("/to-rate", ToRate).RequireAuthorization(Policies.SuppliersRate);
        g.MapPost("/{hireId:guid}/ratings", Rate).RequireAuthorization(Policies.SuppliersRate)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<RatingRequest>();

        return g;
    }

    // ---------- Hires ----------

    private static async Task<IResult> List(Guid eventId, HttpContext http, HtnDbContext db, ICommitteeContext committees, ICurrentUser me, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var isBoard = http.User.IsInRole(Roles.Board);
        var hires = await db.EventSuppliers.AsNoTracking().Where(h => h.EventId == eventId)
            .OrderByDescending(h => h.CreatedAt).ToListAsync(ct);
        return TypedResults.Ok(await HireDtosAsync(db, hires, boardOnly: isBoard, isBoard, committees, me.Id, ct));
    }

    private static async Task<IResult> Create(
        Guid eventId, NewHireRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SupplierId && s.DeactivatedAt == null, ct);
        if (supplier is null) return Problems.Field("supplierId", "Pick an active supplier.");
        var category = await db.LedgerCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CategoryId && c.IsActive, ct);
        if (category is null) return Problems.Field("categoryId", "Pick a budget category.");
        var committee = await db.Committees.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.OwnerCommitteeId && c.EventId == eventId, ct);
        if (committee is null) return Problems.Field("ownerCommitteeId", "Pick a committee of this event.");

        if (await db.EventSuppliers.AsNoTracking()
                .AnyAsync(h => h.EventId == eventId && h.SupplierId == req.SupplierId && h.VoidedAt == null, ct))
            return SuppliersOps.Conflict("This supplier is already hired for this event. Void the existing hire first.", SupplierErrorCodes.AlreadyHired);

        var now = clock.GetUtcNow();
        var hire = EventSupplier.Create(eventId, new NewEventSupplierHire(req.SupplierId!.Value, req.CategoryId!.Value, req.OwnerCommitteeId!.Value, req.Note),
            me.Id!.Value, now);
        db.EventSuppliers.Add(hire);
        audit.Record(me.Id, SupplierAuditActions.HireCreate, nameof(EventSupplier), hire.Id.ToString(),
            after: new { eventId, supplierId = hire.SupplierId, categoryId = hire.CategoryId, ownerCommitteeId = hire.OwnerCommitteeId });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await HireDtoAsync(db, hire, boardOnly: true, true, ct));
    }

    private static async Task<IResult> Update(
        Guid eventId, Guid hireId, UpdateHireRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var hire = await db.EventSuppliers.FirstOrDefaultAsync(h => h.Id == hireId && h.EventId == eventId, ct);
        if (hire is null) return TypedResults.NotFound();
        if (hire.IsVoided) return SuppliersOps.Conflict("This hire has been voided.", SupplierErrorCodes.AlreadyVoided);

        var now = clock.GetUtcNow();
        var before = new
        {
            status = hire.Status.ToString(), hire.ContractAmount, hire.PaidAmount, hire.NextPaymentAmount,
            hire.NextPaymentDue, hire.Note,
        };
        hire.Update(new UpdateEventSupplierHire(req.ContractAmount, req.PaidAmount, req.NextPaymentAmount, req.NextPaymentDue, req.Note), me.Id!.Value, now);
        if (req.Status is { } status)
        {
            try
            {
                hire.Advance(status, me.Id!.Value, now);
            }
            catch (DomainException ex)
            {
                return SuppliersOps.Conflict(ex.Message, SupplierErrorCodes.InvalidTransition);
            }
        }
        audit.Record(me.Id, SupplierAuditActions.HireUpdate, nameof(EventSupplier), hire.Id.ToString(),
            before, new
            {
                status = hire.Status.ToString(), hire.ContractAmount, hire.PaidAmount, hire.NextPaymentAmount,
                hire.NextPaymentDue, hire.Note,
            });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await HireDtoAsync(db, hire, boardOnly: true, true, ct));
    }

    private static async Task<IResult> Void(
        Guid eventId, Guid hireId, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var hire = await db.EventSuppliers.FirstOrDefaultAsync(h => h.Id == hireId && h.EventId == eventId, ct);
        if (hire is null) return TypedResults.NotFound();
        if (hire.IsVoided) return SuppliersOps.Conflict("This hire has already been voided.", SupplierErrorCodes.AlreadyVoided);

        hire.Void(req.Reason!, me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, SupplierAuditActions.HireVoid, nameof(EventSupplier), hire.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await HireDtoAsync(db, hire, boardOnly: true, true, ct));
    }

    /// <summary>
    /// Hard-deletes a hire, but only while it is still <see cref="SupplierHireStatus.Quotation"/> — nothing
    /// irreversible (money, a booking) has happened yet, by analogy with <c>EventInvitation</c>'s <c>NotSent</c>
    /// delete window. Enforced again at the database (<c>Suppliers_Guards</c>).
    /// </summary>
    private static async Task<IResult> Delete(Guid eventId, Guid hireId, HtnDbContext db, IAuditLog audit, ICurrentUser me, CancellationToken ct)
    {
        var hire = await db.EventSuppliers.FirstOrDefaultAsync(h => h.Id == hireId && h.EventId == eventId, ct);
        if (hire is null) return TypedResults.NotFound();
        if (!hire.IsQuotation)
            return SuppliersOps.Conflict("This hire has moved past Quotation and can no longer be deleted. Void it instead.", SupplierErrorCodes.NotQuotation);

        db.EventSuppliers.Remove(hire);
        audit.Record(me.Id, SupplierAuditActions.HireDelete, nameof(EventSupplier), hire.Id.ToString());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ---------- Ratings ----------

    private static async Task<IResult> ToRate(
        Guid eventId, HttpContext http, HtnDbContext db, ICommitteeContext committees, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return TypedResults.NotFound();
        if (clock.GetUtcNow() < ev.EndsAt) return TypedResults.Ok(Array.Empty<EventSupplierDto>());

        var isBoard = http.User.IsInRole(Roles.Board);
        var hires = await db.EventSuppliers.AsNoTracking().Where(h => h.EventId == eventId && h.VoidedAt == null).ToListAsync(ct);
        if (!isBoard)
        {
            var led = (await committees.MembershipsAsync(ct)).Where(m => m.Role == CommitteeRole.Lead).Select(m => m.CommitteeId).ToHashSet();
            hires = hires.Where(h => led.Contains(h.OwnerCommitteeId)).ToList();
        }
        var dtos = await HireDtosAsync(db, hires, boardOnly: isBoard, isBoard, committees, me.Id, ct);
        return TypedResults.Ok(dtos.Where(d => !d.AlreadyRatedByMe).ToList());
    }

    /// <summary>
    /// <c>Suppliers.Rate</c>'s precise, two-layer check (the trickiest part of this slice, per the brief):
    /// <list type="number">
    /// <item>The route carries the coarse policy (<c>.RequireAuthorization(Policies.SuppliersRate)</c>): board, or
    /// any goon who leads something, anywhere. That rejects a plain goon or apprentice with 403 before the hire is
    /// even loaded.</item>
    /// <item>Once the hire is loaded, its real <see cref="EventSupplier.OwnerCommitteeId"/> is known, so the same
    /// policy is re-run against a <see cref="CommitteeScope"/> built from it (<see cref="ScopedAuthorization.DenyAsync"/>),
    /// exactly like <c>Speakers.Manage</c> re-runs against an <see cref="EventScope"/> once the event is known. A
    /// lead of some *other* committee passes step 1 but fails step 2.</item>
    /// </list>
    /// </summary>
    private static async Task<IResult> Rate(
        Guid eventId, Guid hireId, RatingRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var hire = await db.EventSuppliers.FirstOrDefaultAsync(h => h.Id == hireId && h.EventId == eventId, ct);
        if (hire is null) return TypedResults.NotFound();
        if (hire.IsVoided) return SuppliersOps.Conflict("This hire has been voided and can no longer be rated.", SupplierErrorCodes.AlreadyVoided);

        // Step 2 of the two-layer check: the coarse route policy only proved "leads something, anywhere" (or board);
        // this proves it is specifically the hire's own owning committee.
        if (await auth.DenyAsync(http, new CommitteeScope(hire.OwnerCommitteeId), Policies.SuppliersRate) is { } denied) return denied;

        var ev = await db.Events.AsNoTracking().FirstAsync(e => e.Id == eventId, ct);
        var now = clock.GetUtcNow();
        if (now < ev.EndsAt)
            return SuppliersOps.Conflict("Suppliers can only be rated once the event has ended.", SupplierErrorCodes.EventNotEnded);

        if (await db.SupplierRatings.AsNoTracking().AnyAsync(r => r.EventSupplierId == hireId && r.RaterId == me.Id, ct))
            return SuppliersOps.Conflict("You have already rated this supplier for this event.", SupplierErrorCodes.AlreadyRated);

        var rating = SupplierRating.Create(hireId, me.Id!.Value,
            new NewSupplierRating(req.Stars!.Value, req.Tags, req.HireAgain!.Value, req.Note), now);
        db.SupplierRatings.Add(rating);
        audit.Record(me.Id, SupplierAuditActions.Rate, nameof(SupplierRating), rating.Id.ToString(),
            after: new { eventSupplierId = hireId, rating.Stars, hireAgain = rating.HireAgain.ToString() });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(SuppliersViews.Rating(rating));
    }

    // ---------- Helpers ----------

    private static async Task<EventSupplierDto> HireDtoAsync(HtnDbContext db, EventSupplier hire, bool boardOnly, bool isBoard, CancellationToken ct) =>
        (await HireDtosAsync(db, [hire], boardOnly, isBoard, committees: null, callerId: null, ct)).Single();

    internal static async Task<List<EventSupplierDto>> HireDtosAsync(
        HtnDbContext db, IReadOnlyList<EventSupplier> hires, bool boardOnly, bool isBoard, ICommitteeContext? committees, Guid? callerId, CancellationToken ct)
    {
        if (hires.Count == 0) return [];
        var supplierIds = hires.Select(h => h.SupplierId).Distinct().ToList();
        var categoryIds = hires.Select(h => h.CategoryId).Distinct().ToList();
        var committeeIds = hires.Select(h => h.OwnerCommitteeId).Distinct().ToList();
        var hireIds = hires.Select(h => h.Id).ToList();

        var supplierNames = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var categoryNames = await db.LedgerCategories.AsNoTracking().Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var committeeNames = await db.Committees.AsNoTracking().Where(c => committeeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var ratings = await db.SupplierRatings.AsNoTracking().Where(r => hireIds.Contains(r.EventSupplierId)).ToListAsync(ct);
        var ratingsByHire = ratings.GroupBy(r => r.EventSupplierId).ToDictionary(g => g.Key, g => (IReadOnlyList<SupplierRating>)[.. g]);

        var result = new List<EventSupplierDto>(hires.Count);
        foreach (var h in hires)
        {
            var canRate = isBoard || (committees is not null && await committees.IsLeadOfAsync(h.OwnerCommitteeId, ct));
            var alreadyRated = callerId is { } id && ratingsByHire.GetValueOrDefault(h.Id, []).Any(r => r.RaterId == id);
            result.Add(SuppliersViews.Hire(h,
                supplierNames.GetValueOrDefault(h.SupplierId, "(removed supplier)"),
                categoryNames.GetValueOrDefault(h.CategoryId, ""),
                committeeNames.GetValueOrDefault(h.OwnerCommitteeId, ""),
                ratingsByHire.GetValueOrDefault(h.Id, []), boardOnly, canRate, alreadyRated));
        }
        return result;
    }
}
