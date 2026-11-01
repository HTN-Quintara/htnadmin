import { TabNav } from '@/components/ui/Tabs'

/**
 * The three finance screens (frames 24-26). Budgets are per event, so the tab only appears once an event is being
 * looked at; the link is kept in place from the Budgets screen itself.
 */
export function FinanceTabs({ eventId }: { eventId?: string }) {
  const tabs = [
    { to: '/finance', label: 'Cashflow & ledger', end: true },
    ...(eventId ? [{ to: `/finance/budgets/${eventId}`, label: 'Event budget' }] : []),
    { to: '/finance/requests', label: 'Expense requests' },
  ]
  return <TabNav tabs={tabs} aria-label="Finance sections" />
}
