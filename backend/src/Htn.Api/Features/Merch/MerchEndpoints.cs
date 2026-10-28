using System.Text;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Api.Features.Finance;
using Htn.Api.Features.Registrations;
using Htn.Domain.Common;
using Htn.Domain.Merch;
using Htn.Domain.Organization;
using Htn.Domain.Registrations;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Identity;
using Htn.Infrastructure.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Htn.Api.Features.Merch;

/// <summary>
/// Phase 8A: the event-scoped merch catalog, discount settings and POS (buyer search, QR scan, sale). Catalog and
/// discount writes are board-only (<c>Merch.Manage</c>); selling is board, or any committee lead of that event
/// (<c>Merch.Sell</c>) — the same resource-scoped two-step as <c>Suppliers.Rate</c>: the route carries the coarse
/// "leads something, anywhere" gate, and every handler re-runs the policy against the event's real
/// <see cref="EventScope"/> once it has confirmed the event exists. <c>Merch.Sell</c> is a superset of
/// <c>Merch.Manage</c> (both reduce to "is the board" once Manage's only holder is the board), so the read-only
/// dashboard is gated on <c>Merch.Sell</c> alone and only flags <c>canManage</c> for the board in its payload.
/// Stock is checked here for a friendly message, then enforced for real by the <c>merch_sale_lines_stock_decrement</c>
/// trigger (<c>Merch_Guards</c>), which is what actually rules out a race between two concurrent sales for the last
/// unit.
/// </summary>
public static class MerchEndpoints
{
    public const int SalesPageSize = 100;
    public const int BuyerSearchLimit = 20;
    public const int MinNameSearchLength = 3;

    public static RouteGroupBuilder MapMerch(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/events/{eventId:guid}/merch").WithTags("Merch");

        g.MapGet("/items", ListItems).RequireAuthorization(Policies.MerchManage);
        g.MapPost("/items", CreateItem).RequireAuthorization(Policies.MerchManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<MerchItemRequest>();
        g.MapPatch("/items/{itemId:guid}", UpdateItem).RequireAuthorization(Policies.MerchManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<MerchItemRequest>();
        g.MapPost("/items/{itemId:guid}/deactivate", DeactivateItem).RequireAuthorization(Policies.MerchManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();
        g.MapPost("/items/{itemId:guid}/variants", CreateVariant).RequireAuthorization(Policies.MerchManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<MerchVariantRequest>();
        g.MapPatch("/items/{itemId:guid}/variants/{variantId:guid}", UpdateVariant).RequireAuthorization(Policies.MerchManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<MerchVariantAdjustRequest>();
        g.MapPost("/items/{itemId:guid}/variants/{variantId:guid}/deactivate", DeactivateVariant).RequireAuthorization(Policies.MerchManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<ReasonRequest>();

        g.MapGet("/discounts", GetDiscounts).RequireAuthorization(Policies.MerchManage);
        g.MapPut("/discounts", UpdateDiscounts).RequireAuthorization(Policies.MerchManage)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<EventMerchSettingsRequest>();

        g.MapGet("/", Dashboard).RequireAuthorization(Policies.MerchSell);
        g.MapGet("/sales/export", ExportSales).RequireAuthorization(Policies.MerchManage);

        var pos = app.MapGroup("/api/events/{eventId:guid}/pos").WithTags("Pos");
        pos.MapGet("/buyer-search", BuyerSearch).RequireAuthorization(Policies.MerchSell);
        pos.MapPost("/scan", Scan).RequireAuthorization(Policies.MerchSell)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<PosScanRequest>();
        pos.MapPost("/sale", Sell).RequireAuthorization(Policies.MerchSell)
            .RequireRateLimiting(SponsorsRateLimiting.Write).Validate<MerchSaleRequest>();

        return g;
    }

    // ---------- Items / variants ----------

    private static async Task<IResult> ListItems(Guid eventId, HtnDbContext db, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        return TypedResults.Ok(await ItemDtosAsync(db, eventId, ct));
    }

    private static async Task<IResult> CreateItem(
        Guid eventId, MerchItemRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        MerchItem item;
        try
        {
            item = MerchItem.Create(eventId, new NewMerchItem(req.Name!, req.Price!.Value, req.SwatchColor), me.Id!.Value, now);
        }
        catch (DomainException ex)
        {
            return Problems.Field("name", ex.Message);
        }
        db.MerchItems.Add(item);
        audit.Record(me.Id, MerchAuditActions.ItemCreate, nameof(MerchItem), item.Id.ToString(),
            after: new { eventId, item.Name, item.Price, item.SwatchColor });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(MerchViews.Item(item, []));
    }

    private static async Task<IResult> UpdateItem(
        Guid eventId, Guid itemId, MerchItemRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var item = await db.MerchItems.FirstOrDefaultAsync(i => i.Id == itemId && i.EventId == eventId, ct);
        if (item is null) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        var before = new { item.Name, item.Price, item.SwatchColor };
        try
        {
            item.Update(new NewMerchItem(req.Name!, req.Price!.Value, req.SwatchColor), me.Id!.Value, now);
        }
        catch (DomainException ex)
        {
            return Problems.Field("name", ex.Message);
        }
        audit.Record(me.Id, MerchAuditActions.ItemUpdate, nameof(MerchItem), item.Id.ToString(),
            before, new { item.Name, item.Price, item.SwatchColor });
        await db.SaveChangesAsync(ct);
        var variants = await db.MerchVariants.AsNoTracking().Where(v => v.ItemId == item.Id).ToListAsync(ct);
        return TypedResults.Ok(MerchViews.Item(item, variants));
    }

    private static async Task<IResult> DeactivateItem(
        Guid eventId, Guid itemId, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var item = await db.MerchItems.FirstOrDefaultAsync(i => i.Id == itemId && i.EventId == eventId, ct);
        if (item is null) return TypedResults.NotFound();
        if (!item.IsActive) return MerchOps.Conflict("This item is already deactivated.", MerchErrorCodes.AlreadyDeactivated);
        item.Deactivate(req.Reason!, me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, MerchAuditActions.ItemDeactivate, nameof(MerchItem), item.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> CreateVariant(
        Guid eventId, Guid itemId, MerchVariantRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var item = await db.MerchItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.EventId == eventId, ct);
        if (item is null) return TypedResults.NotFound();
        if (!item.IsActive) return MerchOps.Conflict("This item has been deactivated.", MerchErrorCodes.AlreadyDeactivated);

        var now = clock.GetUtcNow();
        MerchVariant variant;
        try
        {
            variant = MerchVariant.Create(itemId, req.Label!, req.StockOnHand!.Value, me.Id!.Value, now);
        }
        catch (DomainException ex)
        {
            return Problems.Field("label", ex.Message);
        }
        if (await db.MerchVariants.AsNoTracking().AnyAsync(v => v.ItemId == itemId && v.Label == variant.Label && v.DeactivatedAt == null, ct))
            return MerchOps.Conflict("A variant with that label already exists for this item.", MerchErrorCodes.DuplicateLabel);

        db.MerchVariants.Add(variant);
        audit.Record(me.Id, MerchAuditActions.VariantCreate, nameof(MerchVariant), variant.Id.ToString(),
            after: new { itemId, variant.Label, variant.StockOnHand });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(MerchViews.Variant(variant));
    }

    private static async Task<IResult> UpdateVariant(
        Guid eventId, Guid itemId, Guid variantId, MerchVariantAdjustRequest req, HtnDbContext db, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var variant = await db.MerchVariants.Join(db.MerchItems.Where(i => i.EventId == eventId), v => v.ItemId, i => i.Id, (v, i) => v)
            .FirstOrDefaultAsync(v => v.Id == variantId && v.ItemId == itemId, ct);
        if (variant is null) return TypedResults.NotFound();

        var now = clock.GetUtcNow();
        var before = new { variant.Label, variant.StockOnHand };
        try
        {
            if (req.Label is { } label) variant.Rename(label, me.Id!.Value, now);
            if (req.StockOnHand is { } stock) variant.AdjustStock(stock, me.Id!.Value, now);
        }
        catch (DomainException ex)
        {
            return Problems.Field("stockOnHand", ex.Message);
        }
        audit.Record(me.Id, MerchAuditActions.VariantUpdate, nameof(MerchVariant), variant.Id.ToString(),
            before, new { variant.Label, variant.StockOnHand });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(MerchViews.Variant(variant));
    }

    private static async Task<IResult> DeactivateVariant(
        Guid eventId, Guid itemId, Guid variantId, ReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var variant = await db.MerchVariants.Join(db.MerchItems.Where(i => i.EventId == eventId), v => v.ItemId, i => i.Id, (v, i) => v)
            .FirstOrDefaultAsync(v => v.Id == variantId && v.ItemId == itemId, ct);
        if (variant is null) return TypedResults.NotFound();
        if (!variant.IsActive) return MerchOps.Conflict("This variant is already deactivated.", MerchErrorCodes.AlreadyDeactivated);
        variant.Deactivate(req.Reason!, me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, MerchAuditActions.VariantDeactivate, nameof(MerchVariant), variant.Id.ToString(), reason: req.Reason);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ---------- Discounts ----------

    private static async Task<IResult> GetDiscounts(Guid eventId, HtnDbContext db, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var settings = await GetOrCreateSettingsAsync(db, eventId, clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(MerchViews.Settings(settings));
    }

    private static async Task<IResult> UpdateDiscounts(
        Guid eventId, EventMerchSettingsRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        var settings = await GetOrCreateSettingsAsync(db, eventId, now, ct);
        var before = new { settings.GoonPct, settings.ApprenticePct, settings.MemberPct, settings.BoardPct };
        try
        {
            settings.Update(req.GoonPct!.Value, req.ApprenticePct!.Value, req.MemberPct!.Value, req.BoardPct!.Value, me.Id!.Value, now);
        }
        catch (DomainException ex)
        {
            return Problems.Field("goonPct", ex.Message);
        }
        audit.Record(me.Id, MerchAuditActions.DiscountsUpdate, nameof(EventMerchSettings), settings.Id.ToString(),
            before, new { settings.GoonPct, settings.ApprenticePct, settings.MemberPct, settings.BoardPct });
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(MerchViews.Settings(settings));
    }

    // ---------- Dashboard / export ----------

    private static async Task<IResult> Dashboard(
        Guid eventId, HttpContext http, HtnDbContext db, IAuthorizationService auth, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.MerchSell) is { } denied) return denied;
        var isBoard = await auth.AllowsAsync(http, new EventScope(eventId), Policies.MerchManage);

        var items = await ItemDtosAsync(db, eventId, ct);
        var sales = await db.MerchSales.AsNoTracking().Where(s => s.EventId == eventId)
            .OrderByDescending(s => s.SoldAt).Take(SalesPageSize).ToListAsync(ct);
        var saleDtos = await SaleDtosAsync(db, sales, ct);

        var variants = await db.MerchVariants.AsNoTracking()
            .Where(v => v.DeactivatedAt == null)
            .Join(db.MerchItems.AsNoTracking().Where(i => i.EventId == eventId), v => v.ItemId, i => i.Id, (v, i) => v)
            .ToListAsync(ct);
        var itemsInStock = variants.Sum(v => v.StockOnHand);
        var lowStockCount = variants.Count(v => v.StockOnHand <= MerchViews.LowStockThreshold);

        var allSales = await db.MerchSales.AsNoTracking().Where(s => s.EventId == eventId).ToListAsync(ct);
        var allSaleIds = allSales.Select(s => s.Id).ToList();
        var itemsSold = await db.MerchSaleLines.AsNoTracking()
            .Where(l => allSaleIds.Contains(l.SaleId)).SumAsync(l => (int?)l.Qty, ct) ?? 0;
        var totalSales = allSales.Sum(s => s.Total);
        var discountsGiven = allSales.Sum(s => s.DiscountAmount);
        var kpi = new MerchKpiDto(allSales.Count, itemsSold, itemsInStock, lowStockCount,
            isBoard ? totalSales : null, isBoard ? discountsGiven : null);

        return TypedResults.Ok(new MerchDashboardDto(isBoard, kpi, items, saleDtos));
    }

    private static async Task<IResult> ExportSales(Guid eventId, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return TypedResults.NotFound();

        var sales = await db.MerchSales.AsNoTracking().Where(s => s.EventId == eventId).ToListAsync(ct);
        var saleIds = sales.Select(s => s.Id).ToList();
        var lines = await db.MerchSaleLines.AsNoTracking().Where(l => saleIds.Contains(l.SaleId)).ToListAsync(ct);
        var linesBySale = lines.GroupBy(l => l.SaleId).ToDictionary(g => g.Key, g => g.ToList());
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var variantIds = lines.Select(l => l.VariantId).Distinct().ToList();
        var itemNames = await db.MerchItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        var variantLabels = await db.MerchVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Label, ct);

        var csv = MerchSaleCsv.Build(sales, linesBySale, itemNames, variantLabels);
        audit.Record(me.Id, "merch_sale.export", "Event", eventId.ToString(), after: new { sales = sales.Count });
        await db.SaveChangesAsync(ct);

        var now = clock.GetUtcNow();
        var name = $"{ev.Slug}-merch-sales-{now.ToOffset(TimeSpan.FromHours(8)):yyyyMMdd}.csv";
        return TypedResults.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", name);
    }

    // ---------- POS ----------

    private static async Task<IResult> BuyerSearch(
        Guid eventId, string? q, HttpContext http, IAuthorizationService auth, HtnDbContext db, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.MerchSell) is { } denied) return denied;

        var term = q?.Trim() ?? "";
        if (term.Length > 100) term = term[..100];
        if (term.Length < MinNameSearchLength) return TypedResults.Ok(Array.Empty<BuyerHitDto>());
        var pattern = "%" + RegistrationsEndpoints.LikeEscape(term) + "%";

        var people = await db.People.AsNoTracking()
            .Where(p => p.IsActive && (
                EF.Functions.ILike(p.FirstName + " " + p.LastName, pattern, @"\")
                || EF.Functions.ILike(p.FirstName + " " + (p.MiddleName ?? "") + " " + p.LastName, pattern, @"\")))
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .Take(BuyerSearchLimit)
            .Select(p => new BuyerHitDto("Person", p.Id, null, p.FullName, p.Level.ToString(), p.Email))
            .ToListAsync(ct);

        var attendees = await db.Attendees.AsNoTracking()
            .Where(a => a.EventId == eventId && (
                EF.Functions.ILike(a.FirstName + " " + a.LastName, pattern, @"\")
                || EF.Functions.ILike(a.FirstName + " " + (a.MiddleName ?? "") + " " + a.LastName, pattern, @"\")))
            .OrderBy(a => a.LastName).ThenBy(a => a.FirstName)
            .Take(BuyerSearchLimit)
            .ToListAsync(ct);

        var attendeeHits = new List<BuyerHitDto>(attendees.Count);
        foreach (var a in attendees)
        {
            var email = Person.NormalizeEmail(a.Email);
            var matched = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.IsActive && p.Email == email, ct);
            attendeeHits.Add(matched is not null
                ? new BuyerHitDto("Person", matched.Id, a.Id, matched.FullName, matched.Level.ToString(), matched.Email)
                : new BuyerHitDto("Attendee", null, a.Id, a.FullName, "Attendee", a.Email));
        }

        return TypedResults.Ok(people.Concat(attendeeHits).Take(BuyerSearchLimit).ToList());
    }

    private static async Task<IResult> Scan(
        Guid eventId, PosScanRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, ITicketToken tickets, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.MerchSell) is { } denied) return denied;

        if (!tickets.TryVerify(req.Payload, out var token) || token.EventId != eventId)
            return Problems.Result(StatusCodes.Status404NotFound, "Unknown code", "That code isn't a valid ticket for this event.", MerchErrorCodes.UnknownCode);

        var attendee = await db.Attendees.AsNoTracking().FirstOrDefaultAsync(a => a.Id == token.AttendeeId && a.EventId == eventId, ct);
        if (attendee is null)
            return Problems.Result(StatusCodes.Status404NotFound, "Unknown code", "That code isn't a valid ticket for this event.", MerchErrorCodes.UnknownCode);

        var email = Person.NormalizeEmail(attendee.Email);
        var matched = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.IsActive && p.Email == email, ct);
        return TypedResults.Ok(matched is not null
            ? new BuyerHitDto("Person", matched.Id, attendee.Id, matched.FullName, matched.Level.ToString(), matched.Email)
            : new BuyerHitDto("Attendee", null, attendee.Id, attendee.FullName, "Attendee", attendee.Email));
    }

    private static async Task<IResult> Sell(
        Guid eventId, MerchSaleRequest req, HttpContext http, IAuthorizationService auth, HtnDbContext db, AutoPost autoPost,
        IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, ct)) return TypedResults.NotFound();
        if (await auth.DenyAsync(http, new EventScope(eventId), Policies.MerchSell) is { } denied) return denied;

        // Idempotent replay: a retry of the same POS attempt (network retry, double-tap) resends the same
        // ClientRequestId. If a sale with that key already exists for this event, hand back that sale as-is —
        // no re-validation of stock, no second ledger post. Only a genuinely new key reaches the create path below.
        var existing = await db.MerchSales.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EventId == eventId && s.ClientRequestId == req.ClientRequestId!.Value, ct);
        if (existing is not null)
        {
            var existingLines = await db.MerchSaleLines.AsNoTracking().Where(l => l.SaleId == existing.Id).ToListAsync(ct);
            var existingItemIds = existingLines.Select(l => l.ItemId).Distinct().ToList();
            var existingVariantIds = existingLines.Select(l => l.VariantId).Distinct().ToList();
            var existingItemNames = await db.MerchItems.AsNoTracking().Where(i => existingItemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
            var existingVariantLabels = await db.MerchVariants.AsNoTracking().Where(v => existingVariantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Label, ct);
            var existingLineDtos = existingLines.Select(l => MerchViews.Line(l,
                existingItemNames.GetValueOrDefault(l.ItemId, "(removed item)"), existingVariantLabels.GetValueOrDefault(l.VariantId, "(removed variant)"))).ToList();
            return TypedResults.Ok(MerchViews.Sale(existing, existingLineDtos));
        }

        var variantIds = req.Lines!.Select(l => l.VariantId!.Value).Distinct().ToList();
        var hits = await (
                from v in db.MerchVariants.AsNoTracking()
                join i in db.MerchItems.AsNoTracking().Where(i => i.EventId == eventId) on v.ItemId equals i.Id
                where variantIds.Contains(v.Id)
                select new { v, i })
            .ToListAsync(ct);
        var byVariant = hits.ToDictionary(x => x.v.Id);
        foreach (var variantId in variantIds)
            if (!byVariant.TryGetValue(variantId, out var hit) || !hit.v.IsActive || !hit.i.IsActive)
                return Problems.Field("lines", "Pick an active item variant of this event.");

        var requestedByVariant = req.Lines!.GroupBy(l => l.VariantId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.Qty!.Value));
        foreach (var (variantId, qty) in requestedByVariant)
        {
            var variant = byVariant[variantId].v;
            if (variant.StockOnHand < qty)
                return MerchOps.Conflict($"Only {variant.StockOnHand} left of \"{variant.Label}\".", MerchErrorCodes.InsufficientStock);
        }

        Person? person = null;
        Attendee? attendee = null;
        if (req.BuyerPersonId is { } pid)
        {
            person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid && p.IsActive, ct);
            if (person is null) return Problems.Field("buyerPersonId", "Pick an active person.");
        }
        if (req.BuyerAttendeeId is { } aid)
        {
            attendee = await db.Attendees.AsNoTracking().FirstOrDefaultAsync(a => a.Id == aid && a.EventId == eventId, ct);
            if (attendee is null) return Problems.Field("buyerAttendeeId", "Pick an attendee of this event.");
        }
        var buyerName = person?.FullName ?? attendee?.FullName ?? req.BuyerName?.Trim();
        if (string.IsNullOrWhiteSpace(buyerName))
            return Problems.Field("buyerName", "A buyer name is required.", MerchErrorCodes.NoBuyer);
        var buyerLevel = person?.Level;

        var now = clock.GetUtcNow();
        var settings = await GetOrCreateSettingsAsync(db, eventId, now, ct);
        var discountPct = settings.PctFor(buyerLevel);

        var lineInputs = req.Lines!
            .Select(l => new NewMerchSaleLine(byVariant[l.VariantId!.Value].i.Id, l.VariantId!.Value, l.Qty!.Value, byVariant[l.VariantId!.Value].i.Price))
            .ToList();

        MerchSale sale;
        List<MerchSaleLine> lines;
        try
        {
            (sale, lines) = Htn.Domain.Merch.MerchSale.Create(
                eventId, req.ClientRequestId!.Value, person?.Id, attendee?.Id, buyerName!, buyerLevel, lineInputs, discountPct, req.Method!.Value,
                me.Id!.Value, req.Station, now);
        }
        catch (DomainException ex)
        {
            return Problems.Field("lines", ex.Message);
        }

        db.MerchSales.Add(sale);
        db.MerchSaleLines.AddRange(lines);
        await autoPost.MerchSaleAsync(sale, me.Id!.Value, now, ct);
        audit.Record(me.Id, MerchAuditActions.SaleCreate, nameof(MerchSale), sale.Id.ToString(),
            after: new
            {
                sale.EventId, sale.BuyerNameSnapshot, buyerLevel = sale.BuyerLevelSnapshot?.ToString(), sale.Subtotal,
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
            return MerchOps.Conflict("Someone else just took the last stock. Reload and try again.", MerchErrorCodes.InsufficientStock);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Two near-simultaneous retries of the same attempt both missed the pre-check above and both tried to
            // insert; the loser just needs to hand back the winner's row, not fail the request.
            var winner = await db.MerchSales.AsNoTracking()
                .FirstOrDefaultAsync(s => s.EventId == eventId && s.ClientRequestId == req.ClientRequestId!.Value, ct);
            if (winner is null) throw;
            var winnerLines = await db.MerchSaleLines.AsNoTracking().Where(l => l.SaleId == winner.Id).ToListAsync(ct);
            var winnerLineDtos = winnerLines.Select(l => MerchViews.Line(l,
                byVariant.TryGetValue(l.VariantId, out var h) ? h.i.Name : "(removed item)",
                byVariant.TryGetValue(l.VariantId, out var h2) ? h2.v.Label : "(removed variant)")).ToList();
            return TypedResults.Ok(MerchViews.Sale(winner, winnerLineDtos));
        }

        var lineDtos = lines.Select(l => MerchViews.Line(l, byVariant[l.VariantId].i.Name, byVariant[l.VariantId].v.Label)).ToList();
        return TypedResults.Ok(MerchViews.Sale(sale, lineDtos));
    }

    // ---------- Helpers ----------

    private static async Task<EventMerchSettings> GetOrCreateSettingsAsync(HtnDbContext db, Guid eventId, DateTimeOffset at, CancellationToken ct)
    {
        var local = db.EventMerchSettings.Local.FirstOrDefault(s => s.EventId == eventId);
        if (local is not null) return local;
        var settings = await db.EventMerchSettings.FirstOrDefaultAsync(s => s.EventId == eventId, ct);
        if (settings is not null) return settings;
        var org = await db.OrgSettings.AsNoTracking().SingleAsync(s => s.Id == Htn.Domain.Settings.OrgSettings.SingletonId, ct);
        settings = EventMerchSettings.Default(eventId, org.GoonMerchDiscountPct, org.ApprenticeMerchDiscountPct, org.MemberMerchDiscountPct, org.BoardMerchDiscountPct, at);
        db.EventMerchSettings.Add(settings);
        return settings;
    }

    private static async Task<List<MerchItemDto>> ItemDtosAsync(HtnDbContext db, Guid eventId, CancellationToken ct)
    {
        var items = await db.MerchItems.AsNoTracking().Where(i => i.EventId == eventId).OrderBy(i => i.Name).ToListAsync(ct);
        var itemIds = items.Select(i => i.Id).ToList();
        var variants = await db.MerchVariants.AsNoTracking().Where(v => itemIds.Contains(v.ItemId)).ToListAsync(ct);
        return [.. items.Select(i => MerchViews.Item(i, variants))];
    }

    private static async Task<List<MerchSaleDto>> SaleDtosAsync(HtnDbContext db, IReadOnlyList<MerchSale> sales, CancellationToken ct)
    {
        if (sales.Count == 0) return [];
        var saleIds = sales.Select(s => s.Id).ToList();
        var lines = await db.MerchSaleLines.AsNoTracking().Where(l => saleIds.Contains(l.SaleId)).ToListAsync(ct);
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var variantIds = lines.Select(l => l.VariantId).Distinct().ToList();
        var itemNames = await db.MerchItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        var variantLabels = await db.MerchVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Label, ct);
        var linesBySale = lines.GroupBy(l => l.SaleId).ToDictionary(g => g.Key, g => g.ToList());
        return [.. sales.Select(s => MerchViews.Sale(s,
            [.. linesBySale.GetValueOrDefault(s.Id, []).Select(l =>
                MerchViews.Line(l, itemNames.GetValueOrDefault(l.ItemId, "(removed item)"), variantLabels.GetValueOrDefault(l.VariantId, "(removed variant)")))]))];
    }
}
