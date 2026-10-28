using Htn.Domain.PopUps;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.PopUps;

/// <summary>Projections from the pop-up entities to the DTOs the screens render.</summary>
public static class PopUpViews
{
    public static async Task<PopUpDto> DetailAsync(HtnDbContext db, PopUpStore p, bool canManage, bool canSell, CancellationToken ct)
    {
        var committeeName = await db.Committees.AsNoTracking().Where(c => c.Id == p.OwnerCommitteeId)
            .Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "(removed committee)";
        var venueOrgName = p.VenueDirectoryOrgId is { } orgId
            ? await db.DirectoryOrgs.AsNoTracking().Where(o => o.Id == orgId).Select(o => o.Name).FirstOrDefaultAsync(ct)
            : null;

        var team = await (
            from m in db.PopUpTeamMembers.AsNoTracking()
            join person in db.People.AsNoTracking() on m.PersonId equals person.Id
            where m.PopUpId == p.Id
            select new PopUpTeamMemberDto(person.Id, person.FullName)).ToListAsync(ct);

        var allocations = await db.PopUpAllocations.AsNoTracking().Where(a => a.PopUpId == p.Id).ToListAsync(ct);
        var variantIds = allocations.Select(a => a.VariantId).ToList();
        var variants = await db.MerchVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToListAsync(ct);
        var itemIds = variants.Select(v => v.ItemId).Distinct().ToList();
        var items = await db.MerchItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);

        var allocationDtos = new List<PopUpAllocationDto>(allocations.Count);
        foreach (var a in allocations)
        {
            var variant = variants.FirstOrDefault(v => v.Id == a.VariantId);
            var itemName = variant is not null && items.TryGetValue(variant.ItemId, out var item) ? item.Name : "(removed item)";
            var sold = await PopUpOps.SoldQtyAsync(db, p.Id, a.VariantId, ct);
            int? missing = a.QtyReturned is { } returned ? Math.Max(0, a.QtyBrought - sold - returned) : null;
            allocationDtos.Add(new PopUpAllocationDto(
                a.VariantId, itemName, variant?.Label ?? "(removed variant)", a.QtyBrought, sold, a.QtyReturned, missing));
        }

        var sales = await db.MerchSales.AsNoTracking().Where(s => s.PopUpId == p.Id).ToListAsync(ct);
        // Board-only money, same as Suppliers' price/TIN/bank stripping: a store-team cashier (canSell but
        // not canManage) can run the POS without seeing sales totals, discounts given or shrinkage.
        decimal? totalSales = canManage ? sales.Sum(s => s.Total) : null;
        decimal? discountsGiven = canManage ? sales.Sum(s => s.DiscountAmount) : null;
        decimal? cashCounted = canManage ? p.CashCounted : null;
        decimal? missingValue = canManage ? p.MissingValue : null;

        return new PopUpDto(
            p.Id, p.Name, p.VenueDirectoryOrgId, venueOrgName, p.VenueText, p.StartsAt, p.EndsAt, p.FeeAmount,
            p.DiscountMode, p.GoonPct, p.ApprenticePct, p.MemberPct, p.BoardPct, p.OwnerCommitteeId, committeeName,
            p.Status, team, allocationDtos, totalSales, discountsGiven, cashCounted, missingValue,
            p.IsWrittenOff, p.WrittenOffAt, canManage, canSell);
    }
}
