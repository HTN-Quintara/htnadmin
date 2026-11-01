import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardBody } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Pagination } from '@/components/ui/Pagination'
import { Select } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { ToolIcon } from '@/components/ui/icons'
import { buttonClass } from '@/components/ui/button-styles'
import { exportAuditCsvUrl, useAuditQuery } from '../api'

/** The actions the audit tab lists, in the order they matter to a reviewer. */
const actions: { value: string; label: string }[] = [
  { value: '', label: 'Everything' },
  { value: 'ledger.create', label: 'Entry added' },
  { value: 'ledger.approve', label: 'Entry approved' },
  { value: 'ledger.void', label: 'Entry voided' },
  { value: 'ledger.auto_post', label: 'Posted automatically' },
  { value: 'ledger.contra', label: 'Correcting entry' },
  { value: 'ledger.transfer', label: 'Transfer' },
  { value: 'finance.fund_sweep', label: 'Surplus transferred' },
  { value: 'finance.account_reconcile', label: 'Account reconciled' },
  { value: 'finance.month_close', label: 'Month closed' },
  { value: 'finance.month_reopen', label: 'Month reopened' },
  { value: 'finance.budget_save', label: 'Budget saved' },
  { value: 'finance.export_ledger', label: 'Ledger exported' },
  { value: 'finance.export_audit', label: 'Audit exported' },
]

const labels = new Map(actions.map((a) => [a.value, a.label]))

/**
 * Frame 24d: the append-only trail. Read-only for everybody, the CFO included — that is the point of it.
 */
export function AuditView({ canExport }: { canExport: boolean }) {
  const [action, setAction] = useState('')
  const [page, setPage] = useState(1)
  const filter = { action: action || undefined, page }
  const query = useAuditQuery(filter)

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Select
          aria-label="Filter by action"
          value={action}
          onChange={(e) => {
            setAction(e.target.value)
            setPage(1)
          }}
        >
          {actions.map((a) => (
            <option key={a.value} value={a.value}>
              {a.label}
            </option>
          ))}
        </Select>
        {canExport ? (
          <a className={buttonClass('secondary')} href={exportAuditCsvUrl(filter)}>
            Export CSV
          </a>
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
          Could not load the audit trail. {errorMessage(query.error)}
        </Alert>
      ) : query.isPending ? (
        <div className="flex flex-col gap-2" role="status" aria-label="Loading the audit trail…">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : query.data.items.length === 0 ? (
        <Card>
          <EmptyState
            icon={<ToolIcon size={22} />}
            title={action ? 'Nothing matches this action' : 'Nothing recorded yet'}
            description={
              action
                ? 'Pick a different action, or show everything.'
                : 'Every entry, approval, void, reconciliation, close and export shows up here.'
            }
            action={
              action ? (
                <Button variant="secondary" onClick={() => setAction('')}>
                  Show everything
                </Button>
              ) : undefined
            }
          />
        </Card>
      ) : (
        <Card>
          <CardBody className="p-0">
            <Table minWidth={760}>
              <THead>
                <Tr>
                  <Th>When</Th>
                  <Th>Who</Th>
                  <Th>What</Th>
                  <Th>Reason</Th>
                </Tr>
              </THead>
              <TBody>
                {query.data.items.map((row) => (
                  <Tr key={row.id}>
                    <Td>{new Date(row.at).toLocaleString('en-PH', { dateStyle: 'medium', timeStyle: 'short' })}</Td>
                    <Td>{row.actor ?? 'System'}</Td>
                    <Td>
                      <div className="flex flex-col">
                        <span>{labels.get(row.action) ?? row.action}</span>
                        <span className="font-mono text-xs text-gray-500 dark:text-gray-400">
                          {row.entityType} {row.entityId}
                        </span>
                      </div>
                    </Td>
                    <Td>{row.reason ?? '—'}</Td>
                  </Tr>
                ))}
              </TBody>
            </Table>
          </CardBody>
        </Card>
      )}

      {query.data && query.data.total > query.data.pageSize ? (
        <Pagination page={query.data.page} pageSize={query.data.pageSize} total={query.data.total} onPage={setPage} />
      ) : null}
    </div>
  )
}
