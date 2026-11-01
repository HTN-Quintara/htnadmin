import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardBody, CardHeader } from '@/components/ui/Card'
import { Select } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/toast-context'
import { formatPesoSigned } from '@/lib/format'
import { useBalancingQuery, useCloseMonthMutation, useReopenMonthMutation } from '../api'
import type { AccountBalance } from '../schemas'
import { BalancedBadge } from './badges'
import { CloseMonthDialog } from './CloseMonthDialog'
import { ReasonDialog } from './ReasonDialog'
import { ReconcileDialog } from './ReconcileDialog'

/** Frame 24c: the book balance against the statement, account by account, and the month close that depends on it. */
export function BalancingView() {
  const { toast } = useToast()
  const now = new Date()
  const [period, setPeriod] = useState({ year: now.getFullYear(), month: now.getMonth() + 1 })
  const [reconciling, setReconciling] = useState<AccountBalance | null>(null)
  const [closeOpen, setCloseOpen] = useState(false)
  const [reopenOpen, setReopenOpen] = useState(false)

  const query = useBalancingQuery(period)
  const close = useCloseMonthMutation()
  const reopen = useReopenMonthMutation()

  const months = recentMonths(now, 12)

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Select
          aria-label="Month to balance"
          value={`${period.year}-${period.month}`}
          onChange={(e) => {
            const [year, month] = e.target.value.split('-').map(Number)
            setPeriod({ year, month })
          }}
        >
          {months.map((m) => (
            <option key={`${m.year}-${m.month}`} value={`${m.year}-${m.month}`}>
              {m.label}
            </option>
          ))}
        </Select>
        {query.data?.period.canClose ? <Button onClick={() => setCloseOpen(true)}>Close month</Button> : null}
        {query.data?.period.canReopen ? (
          <Button variant="secondary" onClick={() => setReopenOpen(true)}>
            Reopen month
          </Button>
        ) : null}
      </div>

      {query.isError ? (
        <Alert
          tone="red"
          live="alert"
          action={
            <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
              Try again
            </Button>
          }
        >
          Could not load the balances. {errorMessage(query.error)}
        </Alert>
      ) : query.isPending ? (
        <div className="flex flex-col gap-2" role="status" aria-label="Loading the balances…">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-14" />
          ))}
        </div>
      ) : (
        <>
          {query.data.period.isClosed ? (
            <Alert tone="green" live="status">
              {query.data.period.label} is closed
              {query.data.period.closedBy ? ` by ${query.data.period.closedBy}` : ''}. Its entries are frozen.
            </Alert>
          ) : query.data.period.blockers.length > 0 && query.data.canPost ? (
            <Alert tone="yellow" live="status">
              {query.data.period.label} cannot be closed yet: {query.data.period.blockers.map((b) => b.message).join(' ')}
            </Alert>
          ) : null}

          <Card>
            <CardHeader
              title="Accounts"
              description="The books, against the statement or the counted cash."
              as="h3"
            />
            <CardBody className="p-0">
              <Table minWidth={760}>
                <THead>
                  <Tr>
                    <Th>Account</Th>
                    <Th>Opening</Th>
                    <Th>Books say</Th>
                    <Th>Statement says</Th>
                    <Th>Difference</Th>
                    <Th>State</Th>
                    <Th>
                      <span className="sr-only">Actions</span>
                    </Th>
                  </Tr>
                </THead>
                <TBody>
                  {query.data.accounts.map((account) => {
                    const reconciliation = account.lastReconciliation
                    return (
                      <Tr key={account.id}>
                        <Td>
                          <div className="flex flex-col">
                            <span>{account.label}</span>
                            <span className="text-xs text-gray-500 dark:text-gray-400">
                              {account.kind} · {account.accountNumberMasked}
                              {account.isActive ? '' : ' · deactivated'}
                            </span>
                          </div>
                        </Td>
                        <Td numeric>{formatPesoSigned(account.openingBalance)}</Td>
                        <Td numeric>{formatPesoSigned(account.bookBalance)}</Td>
                        <Td numeric>{reconciliation ? formatPesoSigned(reconciliation.statementBalance) : '—'}</Td>
                        <Td numeric>{reconciliation ? formatPesoSigned(reconciliation.difference) : '—'}</Td>
                        <Td>
                          {account.isReconcilable ? (
                            <BalancedBadge reconciled={!!reconciliation} balanced={!!reconciliation?.isBalanced} />
                          ) : (
                            <span className="text-xs text-gray-500 dark:text-gray-400">Not reconciled</span>
                          )}
                        </Td>
                        <Td>
                          {query.data.canPost && account.isReconcilable && account.isActive && !query.data.period.isClosed ? (
                            <Button variant="secondary" size="sm" onClick={() => setReconciling(account)}>
                              {reconciliation ? 'Reconcile again' : 'Reconcile'}
                            </Button>
                          ) : null}
                        </Td>
                      </Tr>
                    )
                  })}
                </TBody>
              </Table>
            </CardBody>
          </Card>
        </>
      )}

      <ReconcileDialog
        account={reconciling}
        period={period}
        open={reconciling !== null}
        onClose={() => setReconciling(null)}
      />

      {query.data ? (
        <CloseMonthDialog
          period={query.data.period}
          open={closeOpen}
          onClose={() => setCloseOpen(false)}
          pending={close.isPending}
          onConfirm={() => {
            if (close.isPending) return
            close.mutate(period, {
              onSuccess: (p) => {
                toast({ tone: 'success', title: `${p.label} is closed.` })
                setCloseOpen(false)
              },
              onError: (e) =>
                toast({ tone: 'error', title: 'Could not close the month.', description: errorMessage(e) }),
            })
          }}
        />
      ) : null}

      <ReasonDialog
        open={reopenOpen}
        onClose={() => setReopenOpen(false)}
        title={`Reopen ${query.data?.period.label ?? 'this month'}`}
        description="Its entries can be voided again. Only the most recently closed month can be reopened."
        label="Why are you reopening it?"
        confirmLabel="Reopen month"
        destructive={false}
        pending={reopen.isPending}
        onConfirm={(reason) => {
          if (reopen.isPending) return
          reopen.mutate(
            { ...period, reason },
            {
              onSuccess: (p) => {
                toast({ tone: 'success', title: `${p.label} is open again.` })
                setReopenOpen(false)
              },
              onError: (e) =>
                toast({ tone: 'error', title: 'Could not reopen the month.', description: errorMessage(e) }),
            },
          )
        }}
      />
    </div>
  )
}

function recentMonths(from: Date, count: number) {
  return Array.from({ length: count }, (_, i) => {
    const d = new Date(from.getFullYear(), from.getMonth() - i, 1)
    return {
      year: d.getFullYear(),
      month: d.getMonth() + 1,
      label: d.toLocaleDateString('en-PH', { month: 'long', year: 'numeric' }),
    }
  })
}
