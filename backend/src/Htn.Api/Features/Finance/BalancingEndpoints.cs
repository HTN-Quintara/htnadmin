using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Common;
using Htn.Domain.Finance;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Finance;

/// <summary>
/// Frame 24's balancing sub-view: for each account the book balance (derived) next to the statement balance or the
/// counted cash (the CFO's), and the difference between them. A month only closes when every active, reconcilable
/// account shows a zero difference for it.
/// </summary>
public static class BalancingEndpoints
{
    public static RouteGroupBuilder MapBalancing(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/finance/accounts").WithTags("Finance");

        g.MapGet("/", List).RequireFinance(FinanceAccess.Board);
        g.MapPost("/{accountId:guid}/reconcile", Reconcile).RequireFinance(FinanceAccess.Post)
            .RequireRateLimiting(FinanceRateLimiting.Write).Validate<ReconcileAccountRequest>();

        return g;
    }

    private static async Task<IResult> List(
        HtnDbContext db, ICurrentUser me, TimeProvider clock, int? year, int? month, CancellationToken ct)
    {
        var today = ManilaTime.ManilaDate(clock.GetUtcNow());
        var (y, m) = (year ?? today.Year, month ?? today.Month);
        if (y is < 2020 or > 2100 || m is < 1 or > 12) return TypedResults.NotFound();
        var window = ManilaTime.Month(y, m);

        // The book balance is taken at the end of the month being reconciled, not today: that is the figure the
        // statement covers.
        var asOf = window.EndExclusive.AddDays(-1);
        var books = await FinanceOps.BookBalancesAsync(db, asOf, ct);

        var accounts = await db.PaymentAccounts.AsNoTracking().OrderBy(a => a.Label).ToListAsync(ct);
        var reconciliations = await db.AccountReconciliations.AsNoTracking()
            .Where(r => r.Year == y && r.Month == m && r.SupersededAt == null)
            .ToListAsync(ct);
        var names = await FinanceOps.ActorNamesAsync(db, [.. reconciliations.Select(r => r.ReconciledById)], ct);

        var rows = accounts.Select(a =>
        {
            var reconciliation = reconciliations.FirstOrDefault(r => r.PaymentAccountId == a.Id);
            return new AccountBalanceDto(
                a.Id, a.Label, a.Kind.ToString(), a.AccountNumberMasked, a.OpeningBalance,
                books.GetValueOrDefault(a.Id), a.IsReconcilable, a.IsActive,
                reconciliation is null ? null : FinanceViews.Reconciliation(reconciliation, names));
        }).ToList();

        var close = await FinanceOps.OpenCloseAsync(db, y, m, ct);
        var blockers = close is null ? await FinanceOps.CloseBlockersAsync(db, window, ct) : [];
        var latest = await FinanceOps.LatestCloseAsync(db, ct);
        var closeNames = close is null ? [] : await FinanceOps.ActorNamesAsync(db, [close.ClosedById], ct);
        var period = FinanceViews.Month(y, m, close, blockers, me.IsCfo,
            latest is not null && close is not null && latest.Id == close.Id, closeNames);

        return TypedResults.Ok(new BalancingDto(y, m, rows, period, me.IsCfo));
    }

    /// <summary>
    /// Records what the statement said. The book balance is snapshotted onto the row, so "was this balanced when the
    /// CFO signed it off?" stays answerable after later entries move the derived figure. Reconciling again supersedes
    /// the previous row rather than editing it.
    /// </summary>
    private static async Task<IResult> Reconcile(
        Guid accountId, ReconcileAccountRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var account = await db.PaymentAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null) return TypedResults.NotFound();
        if (!account.IsReconcilable)
            return FinanceOps.Conflict("This account is not reconciled against a statement.",
                FinanceErrorCodes.NotReconcilable);
        if (!account.IsActive)
            return FinanceOps.Conflict("This account is deactivated.", FinanceErrorCodes.AccountInactive);

        var (y, m) = (req.Year!.Value, req.Month!.Value);
        var window = ManilaTime.Month(y, m);
        if (await FinanceOps.OpenCloseAsync(db, y, m, ct) is not null)
            return FinanceOps.Conflict("That month is closed. Reopen it to reconcile again.", FinanceErrorCodes.MonthClosed);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var book = await FinanceOps.BookBalanceAsync(db, accountId, window.EndExclusive.AddDays(-1), ct);

        var previous = await db.AccountReconciliations
            .FirstOrDefaultAsync(r => r.PaymentAccountId == accountId && r.Year == y && r.Month == m && r.SupersededAt == null, ct);
        previous?.Supersede("Reconciled again", me.Id!.Value, now);

        var reconciliation = AccountReconciliation.Create(
            accountId, y, m, req.StatementBalance!.Value, book, req.Note, me.Id!.Value, now);
        db.AccountReconciliations.Add(reconciliation);

        audit.Record(me.Id, FinanceAuditActions.AccountReconcile, nameof(AccountReconciliation),
            reconciliation.Id.ToString(),
            previous is null ? null : new { previous.StatementBalance, previous.BookBalance, previous.Difference },
            new
            {
                accountId, label = account.Label, year = y, month = m,
                reconciliation.StatementBalance, reconciliation.BookBalance, reconciliation.Difference,
                reconciliation.IsBalanced,
            },
            req.Note);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var names = await FinanceOps.ActorNamesAsync(db, [reconciliation.ReconciledById], ct);
        return TypedResults.Ok(FinanceViews.Reconciliation(reconciliation, names));
    }
}
