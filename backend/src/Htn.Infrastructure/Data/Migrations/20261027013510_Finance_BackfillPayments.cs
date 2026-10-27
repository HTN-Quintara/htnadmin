using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Htn.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 5: puts the payments taken before the ledger existed into it, so balances are right from the first day
    /// the balancing screen is used and the CFO does not have to key in opening balances by hand.
    /// <list type="bullet">
    /// <item>One event fund per event that has money, then one <c>Posted</c> entry per non-reverted payment, under the
    /// seeded <c>tickets</c> category and dated by the Manila calendar date of <c>received_at</c>.</item>
    /// <item>Comps and zero-amount payments are skipped: no money moved.</item>
    /// <item>The registrant's newest proof of payment becomes the entry's receipt when there is one.</item>
    /// <item>The buyer's participant id only decorates the description, so it is joined loosely: a payment is never
    /// skipped for want of one, because that would quietly leave money out of the opening books.</item>
    /// <item>Idempotent: both inserts rely on <c>ux_funds_event</c> and <c>ux_ledger_entries_source</c> with
    /// <c>ON CONFLICT DO NOTHING</c>, so re-running this (or a half-rolled-back <c>Up</c>) inserts nothing new.</item>
    /// <item>Audited as a single <c>finance.backfill</c> entry carrying the row count — the audit log is append-only,
    /// and the summary is the right granularity for a one-off data move.</item>
    /// </list>
    /// Ids come from <c>gen_random_uuid()</c> rather than the usual <c>Guid.CreateVersion7</c>, because a migration
    /// has no C# generator. The ids are opaque and never shown, so only their uniqueness matters.
    /// <para>
    /// It runs before <c>Finance_Guards</c> on purpose: the closed-period and append-only triggers do not exist yet,
    /// so the historical rows go in without having to be exempted from them.
    /// </para>
    /// </summary>
    public partial class Finance_BackfillPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- A fund for every event that actually took money.
                INSERT INTO funds (id, kind, event_id, name, created_at)
                SELECT gen_random_uuid(), 'Event', e.id, left(e.name, 120), now()
                FROM events e
                WHERE EXISTS (
                    SELECT 1 FROM registrations r
                    JOIN payments p ON p.registration_id = r.id
                    WHERE r.event_id = e.id AND p.reverted_at IS NULL AND p.amount > 0)
                ON CONFLICT DO NOTHING;

                -- One posted entry per live payment. The participant id of the buyer makes the row traceable.
                WITH inserted AS (
                    INSERT INTO ledger_entries
                        (id, fund_id, payment_account_id, category_id, entry_date, direction, amount, reference,
                         description, receipt_file_id, status, source, source_id, entered_by_id, entered_at,
                         approved_by_id, approved_at)
                    SELECT gen_random_uuid(),
                           f.id,
                           p.payment_account_id,
                           '0199c4a0-0000-7000-8000-000000000011'::uuid,
                           ((p.received_at AT TIME ZONE 'UTC') + interval '8 hours')::date,
                           'In',
                           p.amount,
                           p.reference,
                           left(coalesce('Ticket payment · ' || buyer.participant_id, 'Ticket payment'), 300),
                           (SELECT pf.file_id FROM payment_proofs pf
                            WHERE pf.registration_id = r.id ORDER BY pf.uploaded_at DESC LIMIT 1),
                           'Posted',
                           'TicketPayment',
                           p.id,
                           p.marked_by_id,
                           p.marked_at,
                           p.marked_by_id,
                           p.marked_at
                    FROM payments p
                    JOIN registrations r ON r.id = p.registration_id
                    JOIN funds f ON f.kind = 'Event' AND f.event_id = r.event_id
                    LEFT JOIN attendees buyer ON buyer.registration_id = r.id AND buyer.sort_order = 0
                    WHERE p.reverted_at IS NULL AND p.amount > 0
                    ON CONFLICT DO NOTHING
                    RETURNING 1)
                INSERT INTO audit_entries (id, at, actor_id, action, entity_type, entity_id, before_json, after_json, reason)
                SELECT gen_random_uuid(), now(), NULL, 'finance.backfill', 'LedgerEntry', 'Finance_BackfillPayments',
                       NULL,
                       json_build_object('entries', (SELECT count(*) FROM inserted))::jsonb,
                       'Phase 5 migration: one posted entry per non-reverted, non-zero payment.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the rows this migration created: the backfilled ticket entries, and the event funds that are left
            // with none. A ledger entry added since by the application is never touched.
            migrationBuilder.Sql("""
                DELETE FROM ledger_entries e
                WHERE e.source = 'TicketPayment'
                  AND e.month_close_id IS NULL
                  AND EXISTS (SELECT 1 FROM payments p WHERE p.id = e.source_id AND p.reverted_at IS NULL);

                DELETE FROM funds f
                WHERE f.kind = 'Event'
                  AND NOT EXISTS (SELECT 1 FROM ledger_entries e WHERE e.fund_id = f.id);
                """);
            // The 'finance.backfill' audit row is deliberately left behind: audit_entries is append-only, and that a
            // backfill ran once is true whether or not the schema was later rolled back.
        }
    }
}
