using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Finance;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Finance;

/// <summary>
/// Frame 25: an event's budget against its actuals. "Only the CFO edits them; there is no board approval", so the
/// control is history instead of sign-off — every save writes a new immutable version with a reason and emails the
/// board. Actuals are never stored: they are the posted entries of the event's fund, matched to a line by category.
/// <para>
/// The "suppliers for this event" half of the frame arrives with the directory slice (Phase 7); this ships the budget
/// half, and the screen shows an empty state for the other.
/// </para>
/// </summary>
public static class BudgetEndpoints
{
    public static RouteGroupBuilder MapBudgets(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/finance/budgets").WithTags("Finance");

        g.MapGet("/{eventId:guid}", Detail).RequireFinance(FinanceAccess.Board);
        g.MapGet("/{eventId:guid}/versions/{versionId:guid}", Version).RequireFinance(FinanceAccess.Board);
        g.MapPut("/{eventId:guid}", Save).RequireFinance(FinanceAccess.Post)
            .RequireRateLimiting(FinanceRateLimiting.Write).Validate<SaveBudgetRequest>();

        return g;
    }

    private static async Task<IResult> Detail(
        Guid eventId, HtnDbContext db, ICurrentUser me, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().Where(e => e.Id == eventId)
            .Select(e => new { e.Id, e.Name }).FirstOrDefaultAsync(ct);
        if (ev is null) return TypedResults.NotFound();

        var budget = await db.EventBudgets.AsNoTracking().FirstOrDefaultAsync(b => b.EventId == eventId, ct);
        return TypedResults.Ok(await BuildAsync(db, eventId, ev.Name, budget, budget?.CurrentVersionId, me, ct));
    }

    /// <summary>One earlier version, so the history drawer can show what the numbers were then.</summary>
    private static async Task<IResult> Version(
        Guid eventId, Guid versionId, HtnDbContext db, ICurrentUser me, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().Where(e => e.Id == eventId)
            .Select(e => new { e.Id, e.Name }).FirstOrDefaultAsync(ct);
        if (ev is null) return TypedResults.NotFound();
        var budget = await db.EventBudgets.AsNoTracking().FirstOrDefaultAsync(b => b.EventId == eventId, ct);
        if (budget is null) return TypedResults.NotFound();
        if (!await db.EventBudgetVersions.AsNoTracking().AnyAsync(v => v.Id == versionId && v.BudgetId == budget.Id, ct))
            return TypedResults.NotFound();

        return TypedResults.Ok(await BuildAsync(db, eventId, ev.Name, budget, versionId, me, ct));
    }

    private static async Task<IResult> Save(
        Guid eventId, SaveBudgetRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var ev = await db.Events.AsNoTracking().Where(e => e.Id == eventId)
            .Select(e => new { e.Id, e.Name }).FirstOrDefaultAsync(ct);
        if (ev is null) return TypedResults.NotFound();

        // Every category must exist and still be in use, or an actual could never reach the line.
        foreach (var (line, index) in req.Lines!.Select((l, i) => (l, i)))
        {
            if (await FinanceOps.CategoryAsync(db, line.CategoryId!.Value, $"lines[{index}].categoryId", ct) is (null, { } error))
                return error;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow();
        var budget = await db.EventBudgets.FirstOrDefaultAsync(b => b.EventId == eventId, ct);
        if (budget is null)
        {
            budget = EventBudget.Create(eventId, now);
            db.EventBudgets.Add(budget);
        }

        var previousVersionId = budget.CurrentVersionId == Guid.Empty ? (Guid?)null : budget.CurrentVersionId;
        var before = previousVersionId is null ? null : await SnapshotAsync(db, previousVersionId.Value, ct);

        var lines = req.Lines!
            .Select(l => new NewBudgetLine(l.CategoryId!.Value, l.Kind!.Value, l.Label!, l.AmountBudgeted!.Value,
                l.SortOrder ?? 0, l.Note))
            .ToList();
        var (version, rows) = budget.SaveVersion(lines, req.Reason!, me.Id!.Value, now);
        db.EventBudgetVersions.Add(version);
        db.EventBudgetLines.AddRange(rows);

        audit.Record(me.Id, FinanceAuditActions.BudgetSave, nameof(EventBudget), budget.Id.ToString(),
            before,
            new
            {
                eventId,
                versionId = version.Id,
                version.VersionNumber,
                lines = rows.Select(r => new { r.CategoryId, kind = r.Kind.ToString(), r.Label, r.AmountBudgeted }),
            },
            req.Reason);

        // "Every save creates a new version with a reason, and the board is notified." One message per recipient, so
        // one bad address cannot hold up the rest.
        foreach (var personId in await BoardPersonIdsAsync(db, me.PersonId, ct))
            db.Outbox.Add(OutboxProcessor.BudgetSavedEmail(eventId, version.Id, personId, now));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return TypedResults.Ok(await BuildAsync(db, eventId, ev.Name, budget, version.Id, me, ct));
    }

    // ---------- Helpers ----------

    private static async Task<EventBudgetDto> BuildAsync(
        HtnDbContext db, Guid eventId, string eventName, EventBudget? budget, Guid? versionId,
        ICurrentUser me, CancellationToken ct)
    {
        var fund = await db.Funds.AsNoTracking().FirstOrDefaultAsync(f => f.EventId == eventId, ct);
        var fundBalance = fund is null ? 0m : await FinanceOps.FundBalanceAsync(db, fund.Id, ct);
        var actuals = fund is null ? [] : await FinanceOps.ActualsByCategoryAsync(db, fund.Id, ct);

        var lines = new List<BudgetLineDto>();
        BudgetVersionDto? version = null;
        var history = new List<BudgetVersionDto>();

        if (budget is not null && versionId is { } vid)
        {
            var versions = await db.EventBudgetVersions.AsNoTracking()
                .Where(v => v.BudgetId == budget.Id)
                .OrderByDescending(v => v.VersionNumber)
                .ToListAsync(ct);
            var names = await FinanceOps.ActorNamesAsync(db, [.. versions.Select(v => v.CreatedById)], ct);
            history = [.. versions.Select(v => FinanceViews.Version(v, names))];
            version = versions.Where(v => v.Id == vid).Select(v => FinanceViews.Version(v, names)).FirstOrDefault();

            var rows = await (
                from l in db.EventBudgetLines.AsNoTracking()
                join c in db.LedgerCategories.AsNoTracking() on l.CategoryId equals c.Id
                where l.VersionId == vid
                orderby l.Kind, l.SortOrder, l.Label
                select new { Line = l, CategoryName = c.Name }).ToListAsync(ct);
            lines = [.. rows.Select(r => FinanceViews.Line(r.Line, r.CategoryName, actuals.GetValueOrDefault(r.Line.CategoryId)))];
        }

        return new EventBudgetDto(budget?.Id, eventId, eventName, fund?.Id, fundBalance, version, lines,
            FinanceViews.Totals(lines), history, me.IsCfo);
    }

    private static async Task<object> SnapshotAsync(HtnDbContext db, Guid versionId, CancellationToken ct)
    {
        var version = await db.EventBudgetVersions.AsNoTracking().FirstAsync(v => v.Id == versionId, ct);
        var lines = await db.EventBudgetLines.AsNoTracking().Where(l => l.VersionId == versionId)
            .Select(l => new { l.CategoryId, Kind = l.Kind.ToString(), l.Label, l.AmountBudgeted })
            .ToListAsync(ct);
        return new { versionId, version.VersionNumber, lines };
    }

    /// <summary>Active board members with a login, except the CFO who just saved.</summary>
    private static async Task<List<Guid>> BoardPersonIdsAsync(HtnDbContext db, Guid? exceptPersonId, CancellationToken ct) =>
        await db.People.AsNoTracking()
            .Where(p => p.Level == Htn.Domain.Organization.PersonLevel.Board && p.IsActive)
            .Where(p => exceptPersonId == null || p.Id != exceptPersonId)
            .Select(p => p.Id)
            .ToListAsync(ct);
}
