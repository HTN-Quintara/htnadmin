using System.Globalization;
using Htn.Domain.Finance;
using Htn.Domain.Organization;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Email.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Htn.Infrastructure.Email;

/// <summary>
/// The two finance emails, built from the current database state when the outbox sends them. Both return null when
/// the message no longer applies — the request was corrected, the person lost their login or left the board — so a
/// stale figure is never delivered.
/// </summary>
public sealed class FinanceEmails(HtnDbContext db, EmailRenderer renderer, IOptions<AppLinkOptions> links)
{
    /// <summary>The CFO's decision on an expense request, to the board member who filed it.</summary>
    public async Task<EmailMessage?> ExpenseRequestDecisionAsync(ExpenseRequestDecisionEmailPayload p)
    {
        var request = await db.ExpenseRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == p.RequestId);
        if (request is null) return null;
        // Only a decided request has anything to report; a cancelled one is the requester's own doing.
        if (request.Status is not (ExpenseRequestStatus.Approved or ExpenseRequestStatus.Rejected or ExpenseRequestStatus.Paid))
            return null;

        var recipient = await RecipientAsync(request.RequestedById);
        if (recipient is null) return null;
        var decidedBy = request.DecidedById is { } decider ? await ActorNameAsync(decider) : null;

        var approved = request.Status != ExpenseRequestStatus.Rejected;
        var decision = approved ? "Approved" : "Rejected";
        var amount = Peso(request.Amount);
        var link = links.Value.ExpenseRequests();
        var logo = await renderer.LogoAsync();
        var html = await renderer.RenderAsync<ExpenseRequestDecisionEmail>(new Dictionary<string, object?>
        {
            [nameof(ExpenseRequestDecisionEmail.FirstName)] = recipient.FirstName,
            [nameof(ExpenseRequestDecisionEmail.Title)] = request.Title,
            [nameof(ExpenseRequestDecisionEmail.Amount)] = amount,
            [nameof(ExpenseRequestDecisionEmail.Decision)] = decision,
            [nameof(ExpenseRequestDecisionEmail.DecidedBy)] = decidedBy ?? "the CFO",
            [nameof(ExpenseRequestDecisionEmail.Approved)] = approved,
            [nameof(ExpenseRequestDecisionEmail.Note)] = request.DecisionReason,
            [nameof(ExpenseRequestDecisionEmail.Link)] = link,
            [nameof(ExpenseRequestDecisionEmail.BrandColor)] = await renderer.BrandColorAsync(),
            [nameof(ExpenseRequestDecisionEmail.LogoCid)] = logo?.ContentId,
        });
        var note = string.IsNullOrWhiteSpace(request.DecisionReason) ? "" : $"\n\n\"{request.DecisionReason}\"";
        var text =
            $"""
            Hi {recipient.FirstName},

            Your expense request "{request.Title}" for {amount} was {decision.ToLowerInvariant()} by {decidedBy ?? "the CFO"}.{note}

            Open expense requests:
            {link}

            {Brand.OrgName}
            """;
        return new EmailMessage(recipient.Email, recipient.FirstName,
            $"Your expense request was {decision.ToLowerInvariant()}", html, text,
            EmailCategories.Finance, logo is null ? null : [logo]);
    }

    /// <summary>"Every save creates a new version with a reason, and the board is notified."</summary>
    public async Task<EmailMessage?> BudgetSavedAsync(BudgetSavedEmailPayload p)
    {
        var version = await db.EventBudgetVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == p.VersionId);
        if (version is null) return null;
        var ev = await db.Events.AsNoTracking().Where(e => e.Id == p.EventId).Select(e => e.Name).SingleOrDefaultAsync();
        if (ev is null) return null;

        // The recipient must still be an active board member with a login: finance is board-only.
        var recipient = await RecipientAsync(p.PersonId, PersonLevel.Board);
        if (recipient is null) return null;
        var savedBy = await ActorNameAsync(version.CreatedById);

        var lines = await db.EventBudgetLines.AsNoTracking().Where(l => l.VersionId == version.Id)
            .Select(l => new { l.Kind, l.AmountBudgeted }).ToListAsync();
        var income = lines.Where(l => l.Kind == BudgetLineKind.Income).Sum(l => l.AmountBudgeted);
        var expenses = lines.Where(l => l.Kind == BudgetLineKind.Expense).Sum(l => l.AmountBudgeted);

        var link = links.Value.Budget(p.EventId);
        var logo = await renderer.LogoAsync();
        var html = await renderer.RenderAsync<BudgetSavedEmail>(new Dictionary<string, object?>
        {
            [nameof(BudgetSavedEmail.FirstName)] = recipient.FirstName,
            [nameof(BudgetSavedEmail.EventName)] = ev,
            [nameof(BudgetSavedEmail.VersionNumber)] = version.VersionNumber,
            [nameof(BudgetSavedEmail.SavedBy)] = savedBy ?? "The CFO",
            [nameof(BudgetSavedEmail.Reason)] = version.Reason,
            [nameof(BudgetSavedEmail.Income)] = Peso(income),
            [nameof(BudgetSavedEmail.Expenses)] = Peso(expenses),
            [nameof(BudgetSavedEmail.Net)] = Peso(income - expenses),
            [nameof(BudgetSavedEmail.Link)] = link,
            [nameof(BudgetSavedEmail.BrandColor)] = await renderer.BrandColorAsync(),
            [nameof(BudgetSavedEmail.LogoCid)] = logo?.ContentId,
        });
        var text =
            $"""
            Hi {recipient.FirstName},

            {savedBy ?? "The CFO"} saved version {version.VersionNumber} of the {ev} budget.

            "{version.Reason}"

            Income budgeted: {Peso(income)}
            Expenses budgeted: {Peso(expenses)}
            Net: {Peso(income - expenses)}

            Open the budget:
            {link}

            {Brand.OrgName}
            """;
        return new EmailMessage(recipient.Email, recipient.FirstName,
            $"{ev} budget updated (v{version.VersionNumber})", html, text,
            EmailCategories.Finance, logo is null ? null : [logo]);
    }

    private sealed record Recipient(string FirstName, string Email);

    private async Task<Recipient?> RecipientAsync(Guid personId, PersonLevel? level = null) =>
        await (
            from pr in db.People.AsNoTracking()
            join u in db.Users.AsNoTracking() on (Guid?)pr.Id equals u.PersonId
            where pr.Id == personId && pr.IsActive && u.IsActive && (level == null || pr.Level == level)
            select new Recipient(pr.FirstName, pr.Email)).FirstOrDefaultAsync();

    /// <summary>User id → "First Last"; the finance actors are stored as user ids, like the rest of the audit trail.</summary>
    private Task<string?> ActorNameAsync(Guid userId) =>
        (from u in db.Users.AsNoTracking()
         join pr in db.People.AsNoTracking() on u.PersonId equals (Guid?)pr.Id
         where u.Id == userId
         select pr.FirstName + " " + pr.LastName).FirstOrDefaultAsync();

    private static string Peso(decimal amount) =>
        "₱" + amount.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
