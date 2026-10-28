using Htn.Api.Common;
using Htn.Domain.PopUps;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.PopUps;

/// <summary>The <c>code</c> extension on every pop-up problem response, mirrored in the frontend.</summary>
public static class PopUpErrorCodes
{
    public const string NotAllowed = "committee_not_allowed";
    public const string InvalidTransition = "invalid_transition";
    public const string InsufficientLeftoverStock = "insufficient_leftover_stock";
    public const string NotWrittenOffEligible = "not_written_off_eligible";
    public const string AlreadyWrittenOff = "already_written_off";
    public const string InsufficientStock = "insufficient_stock";
    public const string NoBuyer = "no_buyer";
    public const string AlreadyCounted = "already_counted";
    public const string AlreadyAllocated = "already_allocated";
}

/// <summary>Audit action strings, <c>&lt;entity&gt;.&lt;verb&gt;</c> like the rest of the system.</summary>
public static class PopUpAuditActions
{
    public const string Create = "pop_up.create";
    public const string Allocate = "pop_up.allocate";
    public const string Open = "pop_up.open";
    public const string Close = "pop_up.close";
    public const string Count = "pop_up.count";
    public const string WriteOff = "pop_up.write_off";
    public const string SaleCreate = "pop_up_sale.create";
    public const string MoveToStore = "merch_item.move_to_store";
}

public static class PopUpOps
{
    public static IResult Conflict(string detail, string code) =>
        Problems.Result(StatusCodes.Status409Conflict, "That cannot be done right now.", detail, code);

    /// <summary>
    /// "Available" leftover stock for a variant: its physical <c>StockOnHand</c> minus the reservation held by
    /// every pop-up that hasn't fully closed (<c>QtyBrought - QtyReturned</c>, with a not-yet-counted
    /// <c>QtyReturned</c> treated as zero — the full brought amount still reserves). This is a query-time
    /// computation, not a stored column, so there is exactly one source of truth: the allocation rows themselves.
    /// <paramref name="excludingPopUpId"/> lets the allocation endpoint compute "available to me" while editing a
    /// pop-up's own still-<c>Planned</c> allocations (not used while adding brand new lines, where it is null).
    /// </summary>
    public static async Task<int> AvailableLeftoverAsync(
        HtnDbContext db, Guid variantId, Guid? excludingPopUpId, CancellationToken ct)
    {
        var variant = await db.MerchVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variantId, ct);
        if (variant is null) return 0;

        var reserved = await (
            from a in db.PopUpAllocations.AsNoTracking()
            join p in db.PopUpStores.AsNoTracking() on a.PopUpId equals p.Id
            where a.VariantId == variantId
                  && p.Status != PopUpStatus.ClosedBalanced && p.Status != PopUpStatus.ClosedMissing
                  && (excludingPopUpId == null || a.PopUpId != excludingPopUpId)
            select a.QtyBrought - (a.QtyReturned ?? 0))
            .ToListAsync(ct);

        return variant.StockOnHand - reserved.Sum();
    }

    /// <summary>Whether a committee is still allowed to run pop-ups right now (re-read live, never cached).</summary>
    public static async Task<bool> CommitteeAllowedAsync(HtnDbContext db, Guid committeeId, CancellationToken ct) =>
        await db.Committees.AsNoTracking().Where(c => c.Id == committeeId).Select(c => c.PopUpsAllowed).SingleOrDefaultAsync(ct);

    /// <summary>Units of this variant actually rung up by this pop-up's own POS sales so far.</summary>
    public static async Task<int> SoldQtyAsync(HtnDbContext db, Guid popUpId, Guid variantId, CancellationToken ct) =>
        await (from l in db.MerchSaleLines.AsNoTracking()
               join s in db.MerchSales.AsNoTracking() on l.SaleId equals s.Id
               where s.PopUpId == popUpId && l.VariantId == variantId
               select l.Qty).SumAsync(q => (int?)q, ct) ?? 0;
}
