using System.Net;
using Htn.Api.Tests.Infrastructure;
using Htn.Domain.Finance;
using Microsoft.EntityFrameworkCore;
using static Htn.Api.Tests.Events.EventTestData;
using static Htn.Api.Tests.Registrations.RegistrationTestData;
using static Htn.Api.Tests.Finance.FinanceTestData;

namespace Htn.Api.Tests.Finance;

/// <summary>
/// The database half of the finance rules, exercised as if the API had been bypassed entirely — a hand-written
/// <c>UPDATE</c> at a psql prompt. For money, "the endpoint checks it" is not enough.
/// </summary>
public sealed class FinanceIntegrityGuardsTests(HtnApiFactory factory) : IClassFixture<HtnApiFactory>
{
    private Task<int> SqlAsync(FormattableString sql) => factory.DbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(sql));

    private async Task AssertRejectedAsync(FormattableString sql, string? message = null)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(sql));
        if (message is not null) Assert.Contains(message, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_ledger_entry_is_never_deleted_edited_or_truncated()
    {
        var s = await SceneAsync(factory);
        var id = await AddEntryOkAsync(s.CfoClient, s.OrgFundId, s.AccountId, s.TicketsCategoryId, "2026-10-05", "In", "2500.00");

        await AssertRejectedAsync($"DELETE FROM ledger_entries WHERE id = {id}", "never deleted");
        await AssertRejectedAsync($"TRUNCATE ledger_entries CASCADE", "reject");

        // Every money fact is frozen for the life of the row.
        await AssertRejectedAsync($"UPDATE ledger_entries SET amount = 1 WHERE id = {id}", "never edited");
        await AssertRejectedAsync($"UPDATE ledger_entries SET direction = 'Out' WHERE id = {id}", "never edited");
        await AssertRejectedAsync($"UPDATE ledger_entries SET entry_date = '2026-09-01' WHERE id = {id}", "never edited");
        await AssertRejectedAsync($"UPDATE ledger_entries SET description = 'edited' WHERE id = {id}", "never edited");
        await AssertRejectedAsync($"UPDATE ledger_entries SET fund_id = {Funds.OrganizationId} , amount = amount WHERE id = {id}");
        await AssertRejectedAsync($"UPDATE ledger_entries SET receipt_file_id = NULL WHERE id = {id}", "never edited");
        await AssertRejectedAsync($"UPDATE ledger_entries SET entered_by_id = NULL WHERE id = {id}", "never edited");

        // An approval, once given, is part of the record.
        await AssertRejectedAsync($"UPDATE ledger_entries SET approved_at = now() WHERE id = {id}", "never changed");
        // And nothing else is editable either.
        await AssertRejectedAsync($"UPDATE ledger_entries SET status = 'Posted' WHERE id = {id}", "is editable");
    }

    [Fact]
    public async Task An_entry_cannot_be_born_voided_posted_without_approval_or_already_closed()
    {
        var s = await SceneAsync(factory);
        await AssertRejectedAsync($"""
            INSERT INTO ledger_entries (id, fund_id, payment_account_id, category_id, entry_date, direction, amount,
                description, source, status, entered_at, voided_at, void_reason)
            VALUES (gen_random_uuid(), {s.OrgFundId}, {s.AccountId}, {LedgerCategories.TicketsId}, '2026-10-05', 'In',
                100, 'x', 'TicketPayment', 'Voided', now(), now(), 'nope')
            """, "never created already voided");

        await AssertRejectedAsync($"""
            INSERT INTO ledger_entries (id, fund_id, payment_account_id, category_id, entry_date, direction, amount,
                description, source, status, entered_at)
            VALUES (gen_random_uuid(), {s.OrgFundId}, {s.AccountId}, {LedgerCategories.TicketsId}, '2026-10-05', 'In',
                100, 'x', 'TicketPayment', 'Posted', now())
            """, "a posted entry carries its approval");

        // Zero and sub-centavo amounts are not movements of money.
        await AssertRejectedAsync($"""
            INSERT INTO ledger_entries (id, fund_id, payment_account_id, category_id, entry_date, direction, amount,
                description, source, status, entered_at)
            VALUES (gen_random_uuid(), {s.OrgFundId}, {s.AccountId}, {LedgerCategories.TicketsId}, '2026-10-05', 'In',
                0, 'x', 'TicketPayment', 'Pending', now())
            """, "ck_ledger_entries_amount");

        // A hand-entered row always carries its receipt.
        await AssertRejectedAsync($"""
            INSERT INTO ledger_entries (id, fund_id, payment_account_id, category_id, entry_date, direction, amount,
                description, source, status, entered_at)
            VALUES (gen_random_uuid(), {s.OrgFundId}, {s.AccountId}, {LedgerCategories.TicketsId}, '2026-10-05', 'Out',
                100, 'x', 'Manual', 'Pending', now())
            """, "ck_ledger_entries_receipt");
    }

    [Fact]
    public async Task One_live_entry_per_source_row_forever()
    {
        var s = await SceneAsync(factory);
        var ev = await PublishedAsync(s.BoardClient);
        var email = HtnApiFactory.UniqueEmail("guard");
        var created = await RegisterOkAsync(factory.NewClient(), ev.Slug, Body(ev.TicketId, [Person(email)]));
        var regId = await RegistrationIdAsync(factory, created.GetProperty("participantIds")[0].GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await s.BoardClient.PostAsync(
            $"/api/events/{ev.Id}/registrations/mark-paid",
            new { ids = new[] { regId }, method = "GCash", paymentAccountId = s.AccountId })).StatusCode);

        var entry = await factory.DbAsync(db => db.LedgerEntries.AsNoTracking()
            .SingleAsync(e => e.Source == LedgerSource.TicketPayment && e.SourceId != null
                              && db.Funds.Any(f => f.Id == e.FundId && f.EventId == ev.Id)));

        // A second live entry for the same payment is impossible, which is what makes auto-posting idempotent.
        await AssertRejectedAsync($"""
            INSERT INTO ledger_entries (id, fund_id, payment_account_id, category_id, entry_date, direction, amount,
                description, source, source_id, status, entered_at, approved_at)
            VALUES (gen_random_uuid(), {entry.FundId}, {s.AccountId}, {LedgerCategories.TicketsId}, '2026-10-05', 'In',
                {entry.Amount}, 'duplicate', 'TicketPayment', {entry.SourceId}, 'Posted', now(), now())
            """, "ux_ledger_entries_source");
    }

    [Fact]
    public async Task A_transfer_is_never_left_one_sided()
    {
        var s = await SceneAsync(factory);

        // A lone leg is refused at commit by the deferred trigger, not at insert time.
        await AssertRejectedAsync($"""
            INSERT INTO ledger_entries (id, fund_id, category_id, entry_date, direction, amount, description,
                source, transfer_group_id, status, entered_at, approved_at)
            VALUES (gen_random_uuid(), {Funds.OrganizationId}, {LedgerCategories.TransferId}, '2026-10-20', 'Out',
                250, 'Lone leg', 'Transfer', gen_random_uuid(), 'Posted', now(), now())
            """, "two opposite legs");

        // Voiding only one side of a real transfer is refused too.
        var eventId = await OnSaleAsync(s.BoardClient);
        await factory.DbAsync(async db =>
        {
            var ev = await db.Events.AsNoTracking().SingleAsync(e => e.Id == eventId);
            db.Funds.Add(Fund.ForEvent(ev, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });
        var fundId = await factory.DbAsync(db => db.Funds.AsNoTracking()
            .Where(f => f.EventId == eventId).Select(f => f.Id).SingleAsync());
        await AddEntryOkAsync(s.CfoClient, fundId, s.AccountId, s.TicketsCategoryId, "2026-10-05", "In", "900.00");
        var transfer = await ApiClient.JsonAsync(await s.CfoClient.PostAsync("/api/finance/ledger/transfers", new
        {
            fromFundId = fundId, toFundId = Funds.OrganizationId, entryDate = "2026-10-20",
            amount = 300.00m, description = "Float",
        }));
        var legId = transfer.GetProperty("id").GetGuid();

        await AssertRejectedAsync(
            $"UPDATE ledger_entries SET status = 'Voided', voided_at = now(), void_reason = 'half' WHERE id = {legId}",
            "two opposite legs");
    }

    [Fact]
    public async Task A_month_close_and_a_reconciliation_are_append_only()
    {
        var s = await SceneAsync(factory);
        // The close row is inserted directly: what is under test is that it can never be edited or deleted
        // afterwards, not the precondition for creating it (MonthCloseTests covers that, in its own database).
        var closeId = Guid.CreateVersion7();
        await SqlAsync($"""
            INSERT INTO month_closes (id, year, month, closed_by_id, closed_at)
            VALUES ({closeId}, 2027, 6, {s.Cfo.UserId!.Value}, now())
            """);
        await AssertRejectedAsync($"DELETE FROM month_closes WHERE id = {closeId}", "never deleted");
        await AssertRejectedAsync($"UPDATE month_closes SET month = 7 WHERE id = {closeId}", "never edited");
        await AssertRejectedAsync($"UPDATE month_closes SET note = 'edited' WHERE id = {closeId}", "never edited");

        // A reconciliation of an open month, through the API.
        Assert.Equal(HttpStatusCode.OK, (await s.CfoClient.PostAsync($"/api/finance/accounts/{s.AccountId}/reconcile",
            new { year = 2027, month = 7, statementBalance = 0 })).StatusCode);
        var reconciliationId = await factory.DbAsync(db => db.AccountReconciliations.AsNoTracking()
            .Where(r => r.PaymentAccountId == s.AccountId && r.Year == 2027 && r.Month == 7).Select(r => r.Id).SingleAsync());
        await AssertRejectedAsync($"DELETE FROM account_reconciliations WHERE id = {reconciliationId}", "never deleted");
        await AssertRejectedAsync(
            $"UPDATE account_reconciliations SET statement_balance = 1 WHERE id = {reconciliationId}", "instead of editing");
    }

    [Fact]
    public async Task A_saved_budget_version_is_frozen_and_funds_and_categories_are_never_deleted()
    {
        var s = await SceneAsync(factory);
        var eventId = await OnSaleAsync(s.BoardClient);
        await s.CfoClient.PutAsync($"/api/finance/budgets/{eventId}", new
        {
            reason = "First draft",
            lines = new[] { new { categoryId = LedgerCategories.WriteOffId, kind = "Expense", label = "Venue", amountBudgeted = 1_000 } },
        });

        var versionId = await factory.DbAsync(db => db.EventBudgetVersions.AsNoTracking()
            .Where(v => db.EventBudgets.Any(b => b.Id == v.BudgetId && b.EventId == eventId))
            .Select(v => v.Id).SingleAsync());
        await AssertRejectedAsync($"UPDATE event_budget_versions SET reason = 'edited' WHERE id = {versionId}");
        await AssertRejectedAsync($"DELETE FROM event_budget_versions WHERE id = {versionId}");
        await AssertRejectedAsync($"UPDATE event_budget_lines SET amount_budgeted = 1 WHERE version_id = {versionId}");
        await AssertRejectedAsync($"DELETE FROM event_budget_lines WHERE version_id = {versionId}");

        await AssertRejectedAsync($"DELETE FROM funds WHERE id = {Funds.OrganizationId}", "never deleted");
        await AssertRejectedAsync($"UPDATE funds SET kind = 'Event' WHERE id = {Funds.OrganizationId}", "never changes");
        await AssertRejectedAsync($"DELETE FROM ledger_categories WHERE id = {LedgerCategories.TicketsId}", "never deleted");
        await AssertRejectedAsync(
            $"UPDATE ledger_categories SET code = 'renamed' WHERE id = {LedgerCategories.TicketsId}", "keeps its code");
        // A system category can never be taken out of use: automatic posting resolves it by id.
        await AssertRejectedAsync($"""
            UPDATE ledger_categories SET is_active = false, deactivated_at = now(), deactivation_reason = 'no'
            WHERE id = {LedgerCategories.TicketsId}
            """, "ck_ledger_categories_system_active");
    }

    [Fact]
    public async Task The_seeded_funds_and_categories_are_exactly_what_the_code_expects()
    {
        // The migration seeds these with literal ids; if they ever drift, auto-posting silently posts nowhere.
        var org = await factory.DbAsync(db => db.Funds.AsNoTracking().SingleAsync(f => f.Id == Funds.OrganizationId));
        Assert.Equal(FundKind.Organization, org.Kind);
        Assert.Null(org.EventId);

        foreach (var (id, code, _, kind, _) in LedgerCategories.Seeded)
        {
            var category = await factory.DbAsync(db => db.LedgerCategories.AsNoTracking().SingleAsync(c => c.Id == id));
            Assert.Equal(code, category.Code);
            Assert.Equal(kind, category.Kind);
            Assert.True(category.IsSystem);
            Assert.True(category.IsActive);
        }

        // Every source the enum admits must be admitted by the check constraint too, including the five that have no
        // producer until Phases 6-8. Adding an enum value without a migration fails here, not in production.
        var constraint = await factory.DbAsync(db => db.Database
            .SqlQuery<string>($"""
                SELECT pg_get_constraintdef(oid) AS "Value" FROM pg_constraint
                WHERE conname = 'ck_ledger_entries_source'
                """)
            .SingleAsync());
        foreach (var source in Enum.GetValues<LedgerSource>())
            Assert.Contains($"'{source}'", constraint, StringComparison.Ordinal);
    }
}
