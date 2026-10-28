using System.Globalization;
using System.Text;
using Htn.Api.Features.Registrations;
using Htn.Domain.Merch;

namespace Htn.Api.Features.Merch;

/// <summary>
/// Flattens each sale's lines into one CSV row per line, mirroring <c>RegistrationCsv</c> including its
/// formula-injection escaping — <c>BuyerNameSnapshot</c> can be free text typed in at the POS for an
/// unresolved walk-in buyer, so it needs the same leading-apostrophe guard as a registrant's name.
/// </summary>
public static class MerchSaleCsv
{
    public static readonly string[] Header =
    [
        "Sold at", "Buyer", "Buyer level", "Item", "Variant", "Qty", "Unit price", "Line total",
        "Sale subtotal", "Discount %", "Discount amount", "Sale total", "Method", "Station",
    ];

    public static string Build(IEnumerable<MerchSale> sales, IReadOnlyDictionary<Guid, List<MerchSaleLine>> linesBySale,
        IReadOnlyDictionary<Guid, string> itemNames, IReadOnlyDictionary<Guid, string> variantLabels)
    {
        var sb = new StringBuilder();
        Line(sb, Header);
        foreach (var s in sales.OrderBy(s => s.SoldAt))
        {
            foreach (var l in linesBySale.GetValueOrDefault(s.Id, []))
            {
                Line(sb,
                [
                    Time(s.SoldAt), s.BuyerNameSnapshot, s.BuyerLevelSnapshot?.ToString(),
                    itemNames.GetValueOrDefault(l.ItemId, "(removed item)"), variantLabels.GetValueOrDefault(l.VariantId, "(removed variant)"),
                    l.Qty.ToString(CultureInfo.InvariantCulture), Money(l.UnitPriceSnapshot), Money(l.LineTotal),
                    Money(s.Subtotal), s.DiscountPct.ToString(CultureInfo.InvariantCulture), Money(s.DiscountAmount),
                    Money(s.Total), s.Method.ToString(), s.Station,
                ]);
            }
        }
        return sb.ToString();
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset at) => at.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static void Line(StringBuilder sb, IReadOnlyList<string?> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(RegistrationCsv.Cell(cells[i]));
        }
        sb.Append("\r\n");
    }
}
