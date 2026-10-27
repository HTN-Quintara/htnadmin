import { Outlet, useParams } from 'react-router'
import { useAuth } from '@/app/auth/auth-context'
import { Alert } from '@/components/ui/Alert'
import { PageHeader } from '@/components/ui/PageHeader'
import { FinanceTabs } from './components/FinanceTabs'

/**
 * The finance shell (frames 24-26), board only — the sidebar item carries a lock, and the API answers 404 rather than
 * 403 to anyone outside finance, so the module's existence is not revealed.
 * <p>
 * The banner is the honest version of "hide what you cannot do": a board member who is not the CFO can read
 * everything and change nothing, and the finance assistant can only add entries. Saying so once, here, is kinder than
 * leaving them to discover it one greyed-out button at a time.
 */
export function FinanceLayout() {
  const { me } = useAuth()
  const { eventId } = useParams()
  const isCfo = me?.isCfo ?? false
  const isAssistant = me?.isFinanceAssistant ?? false

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-4">
        <PageHeader
          title="Finance"
          description={
            isAssistant && !isCfo
              ? 'Add the entries you have receipts for. The CFO approves them.'
              : 'Cashflow, the ledger, balancing and the audit trail.'
          }
        />
        {!isCfo && !isAssistant ? (
          <Alert tone="blue">
            You can view finance and file expense requests. Only the CFO posts entries, reconciles accounts and closes
            months.
          </Alert>
        ) : null}
        {isAssistant && !isCfo ? (
          <Alert tone="blue">
            You can add entries. Each one waits for the CFO to approve it before it counts towards a balance.
          </Alert>
        ) : null}
        <FinanceTabs eventId={eventId} />
      </div>
      <Outlet />
    </div>
  )
}
