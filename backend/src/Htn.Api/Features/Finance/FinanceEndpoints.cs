using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Common;
using Htn.Domain.Finance;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Htn.Api.Features.Finance;

/// <summary>
/// Frame 24's cashflow and audit sub-views, the funds and categories the other screens pick from, and the accounting
/// periods: closing a month, reopening it, and sweeping a finished event's surplus into the Organization fund.
/// </summary>
public static class FinanceEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static RouteGroupBuilder MapFinance(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/finance").WithTags("Finance");

        g.MapGet("/overview", Overview).RequireFinance(FinanceAccess.Board);
        // The assistant needs the funds and categories to fill in the one form they have.
        g.MapGet("/funds", FundList).RequireFinance(FinanceAccess.Ledger);
        g.MapGet("/categories", CategoryList).RequireFinance(FinanceAccess.Ledger);
        g.MapGet("/audit", AuditList).RequireFinance(FinanceAccess.Board);
        g.MapGet("/audit/export.csv", AuditExport).RequireFinance(FinanceAccess.Board);
        g.MapGet("/periods", Periods).RequireFinance(FinanceAccess.Board);
        g.MapPost("/periods/{year:int}/{month:int}/close", CloseMonth).RequireFinance(FinanceAccess.Post)
            .RequireRateLimiting(FinanceRateLimiting.Write).ValidateOptional<CloseMonthRequest>();
        g.MapPost("/periods/{year:int}/{month:int}/reopen", ReopenMonth).RequireFinance(FinanceAccess.Post)
            .RequireRateLimiting(FinanceRateLimiting.Write).Validate<FinanceReasonRequest>();
        g.MapPost("/funds/{fundId:guid}/sweep", SweepSurplus).RequireFinance(FinanceAccess.Post)
            .RequireRateLimiting(FinanceRateLimiting.Write).ValidateOptional<SweepSurplusRequest>();

        return g;
    }

    // ---------- Cashflow ----------

    private static async Task<IResult> Overview(
        HtnDbContext db, ICurrentUser me, TimeProvider clock, DateOnly? from, DateOnly? to, Guid? fundId,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = ManilaTime.ManilaDate(now);
        // Twelve months back to the end of this month, unless the screen asked for a window.
        var start = from ?? new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
        var endExclusive = (to ?? today).AddDays(1);

        var cashflow = await FinanceOps.CashflowAsync(db, start, endExclusive, fundId, ct);
        var pending = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.Status == LedgerStatus.Pending)
            .Where(e => fundId == null || e.FundId == fundId)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Amount = g.Sum(e => e.Amount) })
            .FirstOrDefaultAsync(ct);

        var balances = await FinanceOps.FundBalancesAsync(db, ct);
        var funds = await db.Funds.AsNoTracking().OrderBy(f => f.Kind).ThenBy(f => f.Name).ToListAsync(ct);

        return TypedResults.Ok(new FinanceOverviewDto(
            start,
            endExclusive.AddDays(-1),
            cashflow.Sum(p => p.MoneyIn),
            cashflow.Sum(p => p.MoneyOut),
            cashflow.Sum(p => p.Net),
            pending?.Count ?? 0,
            pending?.Amount ?? 0m,
            cashflow,
            [.. funds.Select(f => FinanceViews.Fund(f, balances.GetValueOrDefault(f.Id), me.IsCfo))],
            me.IsCfo,
            me.IsCfo));
    }

    private static async Task<IResult> FundList(HtnDbContext db, ICurrentUser me, CancellationToken ct)
    {
        var balances = await FinanceOps.FundBalancesAsync(db, ct);
        var funds = await db.Funds.AsNoTracking().OrderBy(f => f.Kind).ThenBy(f => f.Name).ToListAsync(ct);
        return TypedResults.Ok(funds.Select(f => FinanceViews.Fund(f, balances.GetValueOrDefault(f.Id), me.IsCfo)).ToList());
    }

    private static async Task<IResult> CategoryList(HtnDbContext db, bool? includeInactive, CancellationToken ct)
    {
        var query = db.LedgerCategories.AsNoTracking();
        if (includeInactive is not true) query = query.Where(c => c.IsActive);
        var categories = await query.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        return TypedResults.Ok(categories.Select(FinanceViews.Category).ToList());
    }

    // ---------- Audit ----------

    private static async Task<IResult> AuditList(
        HtnDbContext db, string? action, DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
        var query = AuditQuery(db, action, from, to);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var names = await FinanceOps.ActorNamesAsync(db, [.. rows.Where(a => a.ActorId is not null).Select(a => a.ActorId!.Value)], ct);
        return TypedResults.Ok(new FinanceAuditPageDto(
            [.. rows.Select(a => FinanceViews.Audit(a, names))], p, size, total));
    }

    private static async Task<IResult> AuditExport(
        HttpContext http, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock,
        string? action, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var rows = await AuditQuery(db, action, from, to)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(20_000).ToListAsync(ct);
        var names = await FinanceOps.ActorNamesAsync(db, [.. rows.Where(a => a.ActorId is not null).Select(a => a.ActorId!.Value)], ct);
        var csv = FinanceCsv.Audit(rows.Select(a => FinanceViews.Audit(a, names)));

        audit.Record(me.Id, FinanceAuditActions.ExportAudit, nameof(Htn.Domain.Audit.AuditEntry), "export",
            after: new { rows = rows.Count, action, from, to });
        await db.SaveChangesAsync(ct);

        var now = clock.GetUtcNow();
        http.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        http.Response.Headers[HeaderNames.CacheControl] = "private, no-store";
        return TypedResults.File(FinanceCsv.Bytes(csv), "text/csv; charset=utf-8",
            $"htn-finance-audit-{now.ToOffset(TimeSpan.FromHours(8)):yyyyMMdd}.csv");
    }

    /// <summary>Only finance's own actions: the audit tab is the money trail, not the whole system's log.</summary>
    private static IQueryable<Htn.Domain.Audit.AuditEntry> AuditQuery(
        HtnDbContext db, string? action, DateOnly? from, DateOnly? to)
    {
        var query = db.AuditEntries.AsNoTracking().Where(a => FinanceAuditActions.All.Contains(a.Action));
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
        if (from is { } f)
        {
            var start = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), ManilaTime.Offset).ToUniversalTime();
            query = query.Where(a => a.At >= start);
        }
        if (to is { } t)
        {
            var end = new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), ManilaTime.Offset).ToUniversalTime();
            query = query.Where(a => a.At < end);
        }
        return query;
    }

    // ---------- Periods ----------

    private static async Task<IResult> Periods(HtnDbContext db, ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var today = ManilaTime.ManilaDate(clock.GetUtcNow());
        var closes = await db.MonthCloses.AsNoTracking().OrderByDescending(m => m.Year).ThenByDescending(m => m.Month)
            .Take(36).ToListAsync(ct);
        var names = await FinanceOps.ActorNamesAsync(db, [.. closes.Select(c => c.ClosedById)], ct);
        var latest = await FinanceOps.LatestCloseAsync(db, ct);

        // This month and the eleven before it: the window the balancing screen pages through.
        var periods = new List<MonthStatusDto>();
        for (var i = 0; i < 12; i++)
        {
            var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
            var window = ManilaTime.Month(month.Year, month.Month);
            var close = closes.FirstOrDefault(c => c.Year == month.Year && c.Month == month.Month && c.ReopenedAt is null);
            var blockers = close is null ? await FinanceOps.CloseBlockersAsync(db, window, ct) : [];
            periods.Add(FinanceViews.Month(month.Year, month.Month, close, blockers, me.IsCfo,
                latest is not null && close is not null && latest.Id == close.Id, names));
        }
        return TypedResults.Ok(periods);
    }

    /// <summary>
    /// Closes a month. "A month can only be closed when everything is balanced and nothing is pending", so the
    /// blockers are checked first and reported back by code, and every posted entry in the period is then stamped,
    /// which freezes it for good.
    /// </summary>
    private static async Task<IResult> CloseMonth(
        int year, int month, CloseMonthRequest? req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        if (year is < 2020 or > 2100 || month is < 1 or > 12) return TypedResults.NotFound();
        var window = ManilaTime.Month(year, month);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Two CFO tabs must not close the same month at once; the partial unique index is the backstop.
        await FinanceOps.LockPeriodsAsync(db, ct);

        if (await FinanceOps.OpenCloseAsync(db, year, month, ct) is not null)
            return FinanceOps.Conflict("This month is already closed.", FinanceErrorCodes.MonthAlreadyClosed);

        var blockers = await FinanceOps.CloseBlockersAsync(db, window, ct);
        if (blockers.Count > 0)
        {
            var first = blockers[0];
            return Problems.Result(StatusCodes.Status409Conflict, "This month cannot be closed yet.",
                string.Join(" ", blockers.Select(b => b.Message)), first.Code);
        }

        var now = clock.GetUtcNow();
        var close = MonthClose.Create(year, month, req?.Note, me.Id!.Value, now);
        db.MonthCloses.Add(close);

        var entries = await db.LedgerEntries
            .Where(e => e.Status == LedgerStatus.Posted && e.MonthCloseId == null
                        && e.EntryDate >= window.Start && e.EntryDate < window.EndExclusive)
            .ToListAsync(ct);
        foreach (var entry in entries) entry.StampClosed(close.Id);

        audit.Record(me.Id, FinanceAuditActions.MonthClose, nameof(MonthClose), close.Id.ToString(),
            after: new { close.Year, close.Month, entries = entries.Count, close.Note });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await PeriodOkAsync(db, year, month, me, ct);
    }

    /// <summary>
    /// Reopens a month, with a reason. Only the most recently closed month may be reopened, so a certified balance is
    /// never pulled out from under a later one.
    /// </summary>
    private static async Task<IResult> ReopenMonth(
        int year, int month, FinanceReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        if (year is < 2020 or > 2100 || month is < 1 or > 12) return TypedResults.NotFound();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await FinanceOps.LockPeriodsAsync(db, ct);

        var close = await FinanceOps.OpenCloseAsync(db, year, month, ct);
        if (close is null) return FinanceOps.Conflict("This month is not closed.", FinanceErrorCodes.MonthNotClosed);
        var latest = await FinanceOps.LatestCloseAsync(db, ct);
        if (latest is null || latest.Id != close.Id)
            return FinanceOps.Conflict("Only the most recently closed month can be reopened.",
                FinanceErrorCodes.ReopenLatestOnly);

        var now = clock.GetUtcNow();
        close.Reopen(req.Reason!, me.Id!.Value, now);
        var entries = await db.LedgerEntries.Where(e => e.MonthCloseId == close.Id).ToListAsync(ct);
        foreach (var entry in entries) entry.StampReopened();

        audit.Record(me.Id, FinanceAuditActions.MonthReopen, nameof(MonthClose), close.Id.ToString(),
            new { close.Year, close.Month, closed = true }, new { released = entries.Count }, req.Reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await PeriodOkAsync(db, year, month, me, ct);
    }

    /// <summary>
    /// "When an event closes, its surplus transfers to the Organization fund." It is an explicit CFO action rather
    /// than a side effect of closing the event, because an event ends while invoices are still arriving, and closing
    /// an event is something any board member may do — sweeping there would let a non-CFO move money.
    /// </summary>
    private static async Task<IResult> SweepSurplus(
        Guid fundId, SweepSurplusRequest? req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await FinanceOps.LockPeriodsAsync(db, ct);
        var fund = await db.Funds.FirstOrDefaultAsync(f => f.Id == fundId, ct);
        if (fund is null) return TypedResults.NotFound();
        if (fund.Kind != FundKind.Event)
            return FinanceOps.Conflict("Only an event fund is swept into the Organization fund.", FinanceErrorCodes.NoSurplus);
        if (fund.IsSwept)
            return FinanceOps.Conflict("This fund's surplus was already transferred.", FinanceErrorCodes.NoSurplus);

        var surplus = await FinanceOps.FundBalanceAsync(db, fund.Id, ct);
        if (surplus <= 0m)
            return FinanceOps.Conflict("This event fund has nothing left to transfer.", FinanceErrorCodes.NoSurplus);

        var now = clock.GetUtcNow();
        var date = req?.EntryDate ?? ManilaTime.ManilaDate(now);
        if (await FinanceOps.IsClosedAsync(db, date, ct))
            return FinanceOps.Conflict("That month is closed. Pick a date in an open month, or reopen it first.",
                FinanceErrorCodes.MonthClosed);

        var org = await FinanceOps.OrganizationFundAsync(db, ct);
        var description = string.IsNullOrWhiteSpace(req?.Description)
            ? $"Surplus from {fund.Name}"
            : req!.Description!.Trim();
        var (outLeg, inLeg) = LedgerEntry.Transfer(fund, org, LedgerCategories.TransferId, date, surplus,
            description, me.Id, now, LedgerSource.SurplusSweep);
        db.LedgerEntries.AddRange(outLeg, inLeg);
        fund.MarkSwept(outLeg.TransferGroupId!.Value, now);

        audit.Record(me.Id, FinanceAuditActions.FundSweep, nameof(Fund), fund.Id.ToString(),
            after: new
            {
                surplus, toFundId = org.Id, entryDate = date,
                transferGroupId = outLeg.TransferGroupId, entries = new[] { outLeg.Id, inLeg.Id },
            });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var balance = await FinanceOps.FundBalanceAsync(db, fund.Id, ct);
        return TypedResults.Ok(FinanceViews.Fund(fund, balance, me.IsCfo));
    }

    // ---------- Helpers ----------

    private static async Task<IResult> PeriodOkAsync(
        HtnDbContext db, int year, int month, ICurrentUser me, CancellationToken ct)
    {
        var window = ManilaTime.Month(year, month);
        var close = await FinanceOps.OpenCloseAsync(db, year, month, ct);
        var blockers = close is null ? await FinanceOps.CloseBlockersAsync(db, window, ct) : [];
        var latest = await FinanceOps.LatestCloseAsync(db, ct);
        var names = close is null ? [] : await FinanceOps.ActorNamesAsync(db, [close.ClosedById], ct);
        return TypedResults.Ok(FinanceViews.Month(year, month, close, blockers, me.IsCfo,
            latest is not null && close is not null && latest.Id == close.Id, names));
    }
}
