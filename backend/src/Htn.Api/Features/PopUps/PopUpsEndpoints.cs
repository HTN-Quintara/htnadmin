using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Finance;
using Htn.Api.Features.Merch;
using Htn.Domain.Committees;
using Htn.Domain.Common;
using Htn.Domain.Merch;
using Htn.Domain.Organization;
using Htn.Domain.PopUps;
using Htn.Domain.Settings;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.PopUps;

/// <summary>
/// Phase 8C: pop-up stores over leftover merch stock — create/allocate/open/close/count under <c>PopUps.Manage</c>,
/// run the POS under <c>PopUps.Sell</c>, write off missing stock under the CFO-only <c>Finance.Post</c>.
/// <para>
/// <b>Authorization shape.</b> Every write carries the coarse route policy (<see cref="PopUpScopedHandler"/>: board,
/// any lead anywhere, or — for <c>Sell</c> — anyone on any pop-up's team). Once the pop-up's real
/// <see cref="PopUpStore.OwnerCommitteeId"/>/<see cref="PopUpStore.Id"/> are known, the handler re-runs the same
/// policy against a real <see cref="PopUpScope"/> and returns 403 (<see cref="ScopedAuthorization.DenyAsync"/>) —
/// exactly the two-layer shape <c>Suppliers.Rate</c>/<c>Merch.Sell</c> use.
/// </para>
/// </summary>
public static class PopUpsEndpoints
{
    public static RouteGroupBuilder MapPopUps(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/pop-ups").WithTags("PopUps");

        g.MapGet("/", List).RequireAuthorization(Policies.PopUpsSell);
        g.MapGet("/{id:guid}", Detail).RequireAuthorization(Policies.PopUpsSell);
        g.MapPost("/", Create).RequireAuthorization(Policies.PopUpsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<NewPopUpRequest>();
        g.MapPost("/{id:guid}/allocations", Allocate).RequireAuthorization(Policies.PopUpsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<AllocationsRequest>();
        g.MapPost("/{id:guid}/open", Open).RequireAuthorization(Policies.PopUpsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPost("/{id:guid}/close", Close).RequireAuthorization(Policies.PopUpsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write);
        g.MapPost("/{id:guid}/count", Count).RequireAuthorization(Policies.PopUpsManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<CountRequest>();
        g.MapPost("/{id:guid}/write-off", WriteOff).RequireAuthorization(Policies.FinancePost)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<WriteOffRequest>();

        var pos = app.MapGroup("/api/pop-ups/{id:guid}/pos").WithTags("Pos");
        pos.MapGet("/buyer-search", BuyerSearch).RequireAuthorization(Policies.PopUpsSell)
            .RequireRateLimiting(StaffSearchRateLimiting.Merch);
        pos.MapPost("/sale", Sell).RequireAuthorization(Policies.PopUpsSell)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<MerchSaleRequest>();

        app.MapGet("/api/merch/leftover-inventory", LeftoverInventory).WithTags("Merch")
            .RequireAuthorization(Policies.PopUpsManage);
        app.MapPost("/api/events/{eventId:guid}/merch/move-to-store", MoveToStore).WithTags("Merch")
            .RequireAuthorization(Policies.MerchManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<MoveToStoreRequest>();

        return g;
    }

    // ---------- Reads ----------

    private static async Task<IResult> List(HttpContext http, IAuthorizationService auth, HtnDbContext db, CancellationToken ct)
    {
        var pops = await db.PopUpStores.AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        var dtos = new List<PopUpDto>(pops.Count);
        foreach (var p in pops)
        {
            var scope = new PopUpScope(p.OwnerCommitteeId, p.Id, await PopUpOps.CommitteeAllowedAsync(db, p.OwnerCommitteeId, ct));
            var canManage = await auth.AllowsAsync(http, scope, Policies.PopUpsManage);
            var canSell = canManage || await auth.AllowsAsync(http, scope, Policies.PopUpsSell);
            dtos.Add(await PopUpViews.DetailAsync(db, p, canManage, canSell, ct));
        }
        return TypedResults.Ok(dtos);
    }

    private static async Task<IResult> Detail(Guid id, HttpContext http, IAuthorizationService auth, HtnDbContext db, CancellationToken ct)
    {
        var p = await db.PopUpStores.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return TypedResults.NotFound();
        var scope = new PopUpScope(p.OwnerCommitteeId, p.Id, await PopUpOps.CommitteeAllowedAsync(db, p.OwnerCommitteeId, ct));
        var canManage = await auth.AllowsAsync(http, scope, Policies.PopUpsManage);
        var canSell = canManage || await auth.AllowsAsync(http, scope, Policies.PopUpsSell);
        if (!canManage && !canSell) return Problems.Result(StatusCodes.Status403Forbidden, "You don't have access to this.", null, ScopedAuthorization.ForbiddenCode);
        return TypedResults.Ok(await PopUpViews.DetailAsync(db, p, canManage, canSell, ct));
    }

    // ---------- Manage ----------

    private static async Task<IResult> Create(
        NewPopUpRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var committee = await db.Committees.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.OwnerCommitteeId, ct);
        if (committee is null) return Problems.Field("ownerCommitteeId", "Pick a committee.");

        // Precise re-check: the coarse route gate only proved "leads something, anywhere" (or board) — this proves
        // it is specifically this request's own owning committee, and that the committee is still allowed to run
        // pop-ups (both conditions of the PopUps.Manage policy's lead branch).
        if (await auth.DenyAsync(http, new PopUpScope(req.OwnerCommitteeId!.Value, Guid.Empty, committee.PopUpsAllowed), Policies.PopUpsManage) is { } denied) return denied;

        if (req.VenueDirectoryOrgId is { } orgId && !await db.DirectoryOrgs.AsNoTracking().AnyAsync(o => o.Id == orgId, ct))
            return Problems.Field("venueDirectoryOrgId", "Pick an existing directory organization.");

        var teamIds = (req.TeamPersonIds ?? []).Distinct().ToList();
        if (teamIds.Count > 0)
        {
            var activeCount = await db.People.AsNoTracking().CountAsync(p => teamIds.Contains(p.Id) && p.IsActive, ct);
            if (activeCount != teamIds.Count) return Problems.Field("teamPersonIds", "Every team member must be an active person.");
        }

        var now = clock.GetUtcNow();
        PopUpStore p;
        try
        {
            p = PopUpStore.Create(
                new NewPopUpStore(req.Name!, req.VenueDirectoryOrgId, req.VenueText, req.StartsAt!.Value, req.EndsAt!.Value,
                    req.FeeAmount!.Value, req.DiscountMode!.Value, req.GoonPct, req.ApprenticePct, req.MemberPct, req.BoardPct,
                    req.OwnerCommitteeId!.Value),
                me.Id!.Value, now);
        }
        catch (DomainException ex)
        {
            return Problems.Field("name", ex.Message);
        }
        db.PopUpStores.Add(p);
        foreach (var personId in teamIds) db.PopUpTeamMembers.Add(PopUpTeamMember.Create(p.Id, personId, now));

        audit.Record(me.Id, PopUpAuditActions.Create, nameof(PopUpStore), p.Id.ToString(),
            after: new { p.Name, p.OwnerCommitteeId, p.StartsAt, p.EndsAt, p.FeeAmount, discountMode = p.DiscountMode.ToString(), team = teamIds });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await PopUpViews.DetailAsync(db, p, canManage: true, canSell: true, ct));
    }

    private static async Task<IResult> Allocate(
        Guid id, AllocationsRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var p = await db.PopUpStores.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new PopUpScope(p.OwnerCommitteeId, p.Id, await PopUpOps.CommitteeAllowedAsync(db, p.OwnerCommitteeId, ct)), Policies.PopUpsManage) is { } denied) return denied;
        if (p.Status != PopUpStatus.Planned)
            return PopUpOps.Conflict("Stock to bring can only be set while the pop-up is still Planned.", PopUpErrorCodes.InvalidTransition);

        var now = clock.GetUtcNow();
        var lines = req.Lines ?? [];
        foreach (var line in lines)
        {
            var variant = await db.MerchVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == line.VariantId, ct);
            var item = variant is null ? null : await db.MerchItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == variant.ItemId, ct);
            if (variant is null || item is null || !variant.IsActive || !item.IsActive || item.EventId is not null)
                return Problems.Field("lines", "Pick an active variant that's in the Merch store's leftover pool.");
            if (await db.PopUpAllocations.AsNoTracking().AnyAsync(a => a.PopUpId == id && a.VariantId == line.VariantId, ct))
                return PopUpOps.Conflict("This variant is already allocated to this pop-up.", PopUpErrorCodes.AlreadyAllocated);

            var available = await PopUpOps.AvailableLeftoverAsync(db, line.VariantId!.Value, excludingPopUpId: id, ct);
            if (line.Qty!.Value > available)
                return PopUpOps.Conflict($"Only {available} of \"{item.Name} · {variant.Label}\" are available in the leftover pool.",
                    PopUpErrorCodes.InsufficientLeftoverStock);

            db.PopUpAllocations.Add(PopUpAllocation.Create(id, line.VariantId!.Value, line.Qty!.Value, now));
        }

        audit.Record(me.Id, PopUpAuditActions.Allocate, nameof(PopUpStore), p.Id.ToString(),
            after: new { lines = lines.Select(l => new { l.VariantId, l.Qty }) });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.CheckViolation })
        {
            // The C# check above is only a friendly-message optimization; the real guarantee against two
            // concurrent allocations over-reserving the same leftover variant is the DB trigger's row lock
            // (pop_up_allocations_reserve). A genuine race lands here instead of the field error above.
            return PopUpOps.Conflict("Someone else just allocated that stock. Reload and try again.", PopUpErrorCodes.InsufficientLeftoverStock);
        }
        return TypedResults.Ok(await PopUpViews.DetailAsync(db, p, canManage: true, canSell: true, ct));
    }

    private static async Task<IResult> Open(
        Guid id, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var p = await db.PopUpStores.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new PopUpScope(p.OwnerCommitteeId, p.Id, await PopUpOps.CommitteeAllowedAsync(db, p.OwnerCommitteeId, ct)), Policies.PopUpsManage) is { } denied) return denied;
        try
        {
            p.Open(me.Id!.Value, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return PopUpOps.Conflict(ex.Message, PopUpErrorCodes.InvalidTransition);
        }
        audit.Record(me.Id, PopUpAuditActions.Open, nameof(PopUpStore), p.Id.ToString(), after: new { status = p.Status.ToString() });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await PopUpViews.DetailAsync(db, p, canManage: true, canSell: true, ct));
    }

    private static async Task<IResult> Close(
        Guid id, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var p = await db.PopUpStores.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new PopUpScope(p.OwnerCommitteeId, p.Id, await PopUpOps.CommitteeAllowedAsync(db, p.OwnerCommitteeId, ct)), Policies.PopUpsManage) is { } denied) return denied;
        try
        {
            p.Close(me.Id!.Value, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return PopUpOps.Conflict(ex.Message, PopUpErrorCodes.InvalidTransition);
        }
        audit.Record(me.Id, PopUpAuditActions.Close, nameof(PopUpStore), p.Id.ToString(), after: new { status = p.Status.ToString() });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await PopUpViews.DetailAsync(db, p, canManage: true, canSell: true, ct));
    }

    private static async Task<IResult> Count(
        Guid id, CountRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var p = await db.PopUpStores.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new PopUpScope(p.OwnerCommitteeId, p.Id, await PopUpOps.CommitteeAllowedAsync(db, p.OwnerCommitteeId, ct)), Policies.PopUpsManage) is { } denied) return denied;
        if (p.Status != PopUpStatus.NeedsCount)
            return PopUpOps.Conflict("This pop-up isn't awaiting a count.", PopUpErrorCodes.InvalidTransition);

        var allocations = await db.PopUpAllocations.Where(a => a.PopUpId == id).ToListAsync(ct);
        var returnedByVariant = (req.Returned ?? []).ToDictionary(l => l.VariantId!.Value, l => l.QtyReturned!.Value);
        foreach (var a in allocations)
        {
            if (!returnedByVariant.TryGetValue(a.VariantId, out var returned))
                return Problems.Field("returned", "Every allocated variant needs a returned quantity.");
            try
            {
                a.RecordReturned(returned);
            }
            catch (DomainException ex)
            {
                return Problems.Field("returned", ex.Message);
            }
        }

        decimal missingValue = 0m;
        foreach (var a in allocations)
        {
            var sold = await PopUpOps.SoldQtyAsync(db, id, a.VariantId, ct);
            var missingQty = Math.Max(0, a.QtyBrought - sold - (a.QtyReturned ?? 0));
            if (missingQty == 0) continue;
            var variant = await db.MerchVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == a.VariantId, ct);
            var item = variant is null ? null : await db.MerchItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == variant.ItemId, ct);
            missingValue += missingQty * (item?.Price ?? 0m);
        }

        p.CompleteCount(missingValue, req.CashCounted!.Value, me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, PopUpAuditActions.Count, nameof(PopUpStore), p.Id.ToString(),
            after: new { status = p.Status.ToString(), missingValue, cashCounted = req.CashCounted });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await PopUpViews.DetailAsync(db, p, canManage: true, canSell: true, ct));
    }

    private static async Task<IResult> WriteOff(
        Guid id, WriteOffRequest req, HtnDbContext db, AutoPost autoPost, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var p = await db.PopUpStores.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return TypedResults.NotFound();
        if (p.Status != PopUpStatus.ClosedMissing)
            return PopUpOps.Conflict("Only a pop-up closed with something missing can be written off.", PopUpErrorCodes.NotWrittenOffEligible);
        if (p.IsWrittenOff)
            return PopUpOps.Conflict("This pop-up has already been written off.", PopUpErrorCodes.AlreadyWrittenOff);

        var now = clock.GetUtcNow();
        var amount = p.MissingValue ?? 0m;
        try
        {
            p.MarkWrittenOff(req.Reason!, me.Id!.Value, now);
        }
        catch (DomainException ex)
        {
            return PopUpOps.Conflict(ex.Message, PopUpErrorCodes.NotWrittenOffEligible);
        }
        await autoPost.PopUpWriteOffAsync(p, amount, req.Reason!, me.Id!.Value, now, ct);
        audit.Record(me.Id, PopUpAuditActions.WriteOff, nameof(PopUpStore), p.Id.ToString(), after: new { amount }, reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await PopUpViews.DetailAsync(db, p, canManage: true, canSell: true, ct));
    }

    // ---------- POS ----------

    private static async Task<IResult> BuyerSearch(
        Guid id, string? q, HttpContext http, IAuthorizationService auth, HtnDbContext db, CancellationToken ct)
    {
        var p = await db.PopUpStores.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new PopUpScope(p.OwnerCommitteeId, p.Id, await PopUpOps.CommitteeAllowedAsync(db, p.OwnerCommitteeId, ct)), Policies.PopUpsSell) is { } denied) return denied;

        var term = q?.Trim() ?? "";
        if (term.Length > 100) term = term[..100];
        if (term.Length < MerchEndpoints.MinNameSearchLength) return TypedResults.Ok(Array.Empty<BuyerHitDto>());
        var pattern = "%" + Registrations.RegistrationsEndpoints.LikeEscape(term) + "%";

        var people = await db.People.AsNoTracking()
            .Where(x => x.IsActive && (
                EF.Functions.ILike(x.FirstName + " " + x.LastName, pattern, @"\")
                || EF.Functions.ILike(x.FirstName + " " + (x.MiddleName ?? "") + " " + x.LastName, pattern, @"\")))
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Take(MerchEndpoints.BuyerSearchLimit)
            .Select(x => new BuyerHitDto("Person", x.Id, null, x.FullName, x.Level.ToString(), x.Email))
            .ToListAsync(ct);
        return TypedResults.Ok(people);
    }

    /// <summary>
    /// A pop-up POS sale, mirroring <c>MerchEndpoints.Sell</c>'s buyer-resolution/stock-check/idempotency shape, but
    /// scoped to this pop-up's own allocated stock (<see cref="PopUpOps.SoldQtyAsync"/> against each
    /// <see cref="PopUpAllocation"/>, not raw <c>MerchVariant.StockOnHand</c>), its own discount settings, and
    /// posting — through the unchanged <see cref="AutoPost.MerchSaleAsync"/> — to the <b>Organization fund</b>
    /// (<c>sale.PopUpId</c> is set, <c>sale.EventId</c> is null). There is no membership tile here: a pop-up isn't
    /// tied to one main event. Buyer search is People only — a free-text walk-in name is still accepted.
    /// </summary>
    private static async Task<IResult> Sell(
        Guid id, MerchSaleRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, AutoPost autoPost,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var p = await db.PopUpStores.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new PopUpScope(p.OwnerCommitteeId, p.Id, await PopUpOps.CommitteeAllowedAsync(db, p.OwnerCommitteeId, ct)), Policies.PopUpsSell) is { } denied) return denied;
        if (p.Status != PopUpStatus.Open)
            return PopUpOps.Conflict("This pop-up's POS is only open while its status is Open.", PopUpErrorCodes.InvalidTransition);

        var existing = await db.MerchSales.AsNoTracking().FirstOrDefaultAsync(s => s.PopUpId == id && s.ClientRequestId == req.ClientRequestId!.Value, ct);
        if (existing is not null)
        {
            var existingLines = await db.MerchSaleLines.AsNoTracking().Where(l => l.SaleId == existing.Id).ToListAsync(ct);
            var dtos = await LineDtosAsync(db, existingLines, ct);
            return TypedResults.Ok(MerchViews.Sale(existing, dtos));
        }

        var lineRequests = req.Lines ?? [];
        var variantIds = lineRequests.Select(l => l.VariantId!.Value).Distinct().ToList();
        var hits = await (
                from v in db.MerchVariants.AsNoTracking()
                join i in db.MerchItems.AsNoTracking() on v.ItemId equals i.Id
                where variantIds.Contains(v.Id)
                select new { v, i })
            .ToListAsync(ct);
        var byVariant = hits.ToDictionary(x => x.v.Id);
        var allocations = await db.PopUpAllocations.AsNoTracking().Where(a => a.PopUpId == id && variantIds.Contains(a.VariantId))
            .ToDictionaryAsync(a => a.VariantId, ct);

        var requestedByVariant = lineRequests.GroupBy(l => l.VariantId!.Value).ToDictionary(gr => gr.Key, gr => gr.Sum(l => l.Qty!.Value));
        foreach (var (variantId, qty) in requestedByVariant)
        {
            if (!byVariant.TryGetValue(variantId, out var hit) || !hit.v.IsActive || !hit.i.IsActive)
                return Problems.Field("lines", "Pick an active item variant.");
            if (!allocations.TryGetValue(variantId, out var allocation))
                return Problems.Field("lines", "This variant wasn't allocated to this pop-up.");
            var sold = await PopUpOps.SoldQtyAsync(db, id, variantId, ct);
            var remaining = allocation.QtyBrought - sold;
            if (remaining < qty)
                return PopUpOps.Conflict($"Only {remaining} left of \"{hit.v.Label}\" at this pop-up.", PopUpErrorCodes.InsufficientStock);
        }

        Person? person = null;
        if (req.BuyerPersonId is { } pid)
        {
            person = await db.People.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid && x.IsActive, ct);
            if (person is null) return Problems.Field("buyerPersonId", "Pick an active person.");
        }
        var buyerName = person?.FullName ?? req.BuyerName?.Trim();
        if (string.IsNullOrWhiteSpace(buyerName)) return Problems.Field("buyerName", "A buyer name is required.", PopUpErrorCodes.NoBuyer);
        var buyerLevel = person?.Level;

        var org = await db.OrgSettings.AsNoTracking().SingleAsync(s => s.Id == OrgSettings.SingletonId, ct);
        var discountPct = p.PctFor(buyerLevel, org.GoonMerchDiscountPct, org.ApprenticeMerchDiscountPct, org.MemberMerchDiscountPct, org.BoardMerchDiscountPct);

        var lineInputs = lineRequests
            .Select(l => new NewMerchSaleLine(byVariant[l.VariantId!.Value].i.Id, l.VariantId!.Value, l.Qty!.Value, byVariant[l.VariantId!.Value].i.Price))
            .ToList();

        var now = clock.GetUtcNow();
        MerchSale sale;
        List<MerchSaleLine> lines;
        try
        {
            (sale, lines) = MerchSale.Create(
                eventId: null, popUpId: id, req.ClientRequestId!.Value, person?.Id, buyerAttendeeId: null, buyerName!, buyerLevel,
                lineInputs, discountPct, req.Method!.Value, me.Id!.Value, req.Station, membershipFeeIncluded: false, membershipFeeAmount: null, now);
        }
        catch (DomainException ex)
        {
            return Problems.Field("lines", ex.Message);
        }

        db.MerchSales.Add(sale);
        db.MerchSaleLines.AddRange(lines);
        // Phase 8C's one easy-to-get-wrong step: this always posts to the Organization fund (AutoPost.MerchSaleAsync
        // branches on sale.PopUpId, which is set here), never to any event's fund.
        await autoPost.MerchSaleAsync(sale, me.Id!.Value, now, ct);

        audit.Record(me.Id, PopUpAuditActions.SaleCreate, nameof(MerchSale), sale.Id.ToString(),
            after: new
            {
                popUpId = id, sale.BuyerNameSnapshot, buyerLevel = sale.BuyerLevelSnapshot?.ToString(), sale.Subtotal,
                sale.DiscountPct, sale.DiscountAmount, sale.Total, method = sale.Method.ToString(),
                lines = lines.Select(l => new { l.VariantId, l.Qty, l.UnitPriceSnapshot }),
            });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.CheckViolation })
        {
            return PopUpOps.Conflict("Someone else just took the last stock. Reload and try again.", PopUpErrorCodes.InsufficientStock);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } pg
                  && pg.ConstraintName == "ux_merch_sales_pop_up_client_request_id")
        {
            var winner = await db.MerchSales.AsNoTracking().FirstOrDefaultAsync(s => s.PopUpId == id && s.ClientRequestId == req.ClientRequestId!.Value, ct);
            if (winner is null) throw;
            var winnerLines = await db.MerchSaleLines.AsNoTracking().Where(l => l.SaleId == winner.Id).ToListAsync(ct);
            return TypedResults.Ok(MerchViews.Sale(winner, await LineDtosAsync(db, winnerLines, ct)));
        }

        return TypedResults.Ok(MerchViews.Sale(sale, await LineDtosAsync(db, lines, ct)));
    }

    // ---------- Leftover inventory / move-to-store ----------

    private static async Task<IResult> LeftoverInventory(HttpContext http, HtnDbContext db, ICurrentUser me, CancellationToken ct)
    {
        // The route policy only proves the caller leads *some* committee, anywhere (the coarse,
        // resource-less gate every PopUps.Manage check starts from) — this read has no single committee to
        // re-check against, so the precise check has to live here: board, or a lead of a committee the
        // board has actually flagged PopUpsAllowed. Without this, any committee lead could see every
        // leftover item's name, price and quantity regardless of pop-up involvement.
        var isBoard = http.User.IsInRole(Roles.Board);
        if (!isBoard)
        {
            var leadsAllowed = me.PersonId is { } personId && await db.CommitteeMembers.AsNoTracking()
                .Where(m => m.PersonId == personId && m.LeftAt == null && m.Role == CommitteeRole.Lead)
                .Join(db.Committees.AsNoTracking().Where(c => c.IsActive && c.PopUpsAllowed), m => m.CommitteeId, c => c.Id, (m, c) => c.Id)
                .AnyAsync(ct);
            if (!leadsAllowed)
                return Problems.Result(StatusCodes.Status403Forbidden, "You don't have access to this.",
                    "Only the board and leads of a pop-up-allowed committee can see the leftover pool.", ScopedAuthorization.ForbiddenCode);
        }

        var items = await db.MerchItems.AsNoTracking().Where(i => i.EventId == null).ToListAsync(ct);
        var itemIds = items.Select(i => i.Id).ToList();
        var variants = await db.MerchVariants.AsNoTracking().Where(v => itemIds.Contains(v.ItemId) && v.DeactivatedAt == null).ToListAsync(ct);
        var sourceEventIds = items.Select(i => i.LeftoverSourceEventId).Where(e => e != null).Select(e => e!.Value).Distinct().ToList();
        var eventNames = await db.Events.AsNoTracking().Where(e => sourceEventIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Name, ct);

        var result = new List<LeftoverVariantDto>();
        foreach (var v in variants)
        {
            var item = items.First(i => i.Id == v.ItemId);
            var available = await PopUpOps.AvailableLeftoverAsync(db, v.Id, excludingPopUpId: null, ct);
            result.Add(new LeftoverVariantDto(
                item.Id, item.Name, v.Id, v.Label, item.Price, item.LeftoverSourceEventId,
                item.LeftoverSourceEventId is { } sid ? eventNames.GetValueOrDefault(sid, "(removed event)") : "",
                v.StockOnHand, v.StockOnHand - available, available));
        }
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> MoveToStore(
        Guid eventId, MoveToStoreRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var ids = req.VariantIds ?? [];
        var variants = await db.MerchVariants.Where(v => ids.Contains(v.Id)).ToListAsync(ct);
        if (variants.Count != ids.Count) return Problems.Field("variantIds", "Pick existing variants.");
        var itemIds = variants.Select(v => v.ItemId).Distinct().ToList();
        var items = await db.MerchItems.Where(i => itemIds.Contains(i.Id)).ToListAsync(ct);
        foreach (var item in items)
            if (item.EventId != eventId) return Problems.Field("variantIds", "Pick variants that belong to this event.");

        var now = clock.GetUtcNow();
        try
        {
            foreach (var item in items) item.MoveToStore(me.Id!.Value, now);
        }
        catch (DomainException ex)
        {
            return PopUpOps.Conflict(ex.Message, PopUpErrorCodes.InvalidTransition);
        }
        audit.Record(me.Id, PopUpAuditActions.MoveToStore, nameof(MerchItem), eventId.ToString(),
            after: new { eventId, itemIds = items.Select(i => i.Id) });
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<List<MerchSaleLineDto>> LineDtosAsync(HtnDbContext db, IReadOnlyList<MerchSaleLine> lines, CancellationToken ct)
    {
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var variantIds = lines.Select(l => l.VariantId).Distinct().ToList();
        var itemNames = await db.MerchItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        var variantLabels = await db.MerchVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Label, ct);
        return [.. lines.Select(l => MerchViews.Line(l, itemNames.GetValueOrDefault(l.ItemId, "(removed item)"), variantLabels.GetValueOrDefault(l.VariantId, "(removed variant)")))];
    }
}
