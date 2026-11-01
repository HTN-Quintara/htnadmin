using System.Globalization;
using System.Text;

namespace Htn.Api.Features.Finance;

/// <summary>
/// The ledger and audit exports. Same quoting as <see cref="Registrations.RegistrationCsv"/>: RFC 4180 plus a leading
/// apostrophe on anything a spreadsheet would try to run as a formula.
/// </summary>
public static class FinanceCsv
{
    public static readonly string[] LedgerHeader =
    [
        "Date", "Fund", "Account", "Category", "Direction", "Amount", "Signed amount", "Reference", "Description",
        "Status", "Source", "Receipt", "Entered by", "Entered at", "Approved by", "Approved at",
        "Voided by", "Voided at", "Void reason", "Closed month",
    ];

    public static readonly string[] AuditHeader =
    [
        "At", "Actor", "Action", "Entity", "Entity id", "Before", "After", "Reason",
    ];

    public static string Ledger(IEnumerable<LedgerEntryDto> entries)
    {
        var sb = new StringBuilder();
        Line(sb, LedgerHeader);
        foreach (var e in entries)
        {
            Line(sb,
            [
                e.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                e.FundName, e.AccountLabel, e.CategoryName, e.Direction.ToString(),
                Money(e.Amount), Money(e.SignedAmount), e.Reference, e.Description,
                e.Status.ToString(), e.Source.ToString(), e.HasReceipt ? "yes" : "no",
                e.EnteredBy, Time(e.EnteredAt), e.ApprovedBy, e.ApprovedAt is { } a ? Time(a) : null,
                e.VoidedBy, e.VoidedAt is { } v ? Time(v) : null, e.VoidReason,
                e.IsInClosedMonth ? "yes" : "no",
            ]);
        }
        return sb.ToString();
    }

    public static string Audit(IEnumerable<FinanceAuditDto> entries)
    {
        var sb = new StringBuilder();
        Line(sb, AuditHeader);
        foreach (var a in entries)
            Line(sb, [Time(a.At), a.Actor, a.Action, a.EntityType, a.EntityId, a.BeforeJson, a.AfterJson, a.Reason]);
        return sb.ToString();
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset at) =>
        at.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static void Line(StringBuilder sb, IReadOnlyList<string?> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Registrations.RegistrationCsv.Cell(cells[i]));
        }
        sb.Append("\r\n");
    }

    /// <summary>UTF-8 with a BOM, so Excel opens peso signs and accented names correctly.</summary>
    public static byte[] Bytes(string csv) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)];
}
