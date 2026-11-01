import { useState } from 'react'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardBody } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { FilterChip } from '@/components/ui/FilterChip'
import { Pagination } from '@/components/ui/Pagination'
import { Skeleton } from '@/components/ui/Spinner'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { WalletIcon } from '@/components/ui/icons'
import { formatDate, formatPeso } from '@/lib/format'
import { useBalancingQuery, useRequestsQuery } from './api'
import type { ExpenseRequest, RequestStatus } from './schemas'
import { RequestStatusBadge } from './components/badges'
import { RequestDialog } from './components/RequestDialog'
import { RequestDrawer } from './components/RequestDrawer'

const chips: { value: RequestStatus | 'all'; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'Pending', label: 'Waiting' },
  { value: 'Approved', label: 'Approved' },
  { value: 'Paid', label: 'Paid' },
  { value: 'Rejected', label: 'Rejected' },
]

/** Frame 26: expense requests, with the detail in a drawer as the design system prescribes for a record. */
export function RequestsPage() {
  const canRequest = useCan(Policies.FinanceRequest)
  const [status, setStatus] = useState<RequestStatus | 'all'>('all')
  const [page, setPage] = useState(1)
  const [filing, setFiling] = useState(false)
  const [selected, setSelected] = useState<ExpenseRequest | null>(null)

  const query = useRequestsQuery({ status: status === 'all' ? undefined : status, page })
  const now = new Date()
  const balancing = useBalancingQuery({ year: now.getFullYear(), month: now.getMonth() + 1 })
  const accounts = (balancing.data?.accounts ?? []).map((a) => ({ id: a.id, label: a.label, isActive: a.isActive }))
  const filtered = status !== 'all'

  // The drawer shows the freshest copy, so approving leaves the panel in step with the list behind it.
  const current = selected ? (query.data?.items.find((r) => r.id === selected.id) ?? selected) : null

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          {chips.map((chip) => (
            <FilterChip
              key={chip.value}
              selected={status === chip.value}
              onClick={() => {
                setStatus(chip.value)
                setPage(1)
              }}
            >
              {chip.label}
            </FilterChip>
          ))}
        </div>
        {canRequest ? <Button onClick={() => setFiling(true)}>New request</Button> : null}
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
          Could not load the requests. {errorMessage(query.error)}
        </Alert>
      ) : query.isPending ? (
        <div className="flex flex-col gap-2" role="status" aria-label="Loading the requests…">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : query.data.items.length === 0 ? (
        <Card>
          <EmptyState
            icon={<WalletIcon size={22} />}
            title={filtered ? 'Nothing matches this filter' : 'No requests yet'}
            description={
              filtered
                ? 'Pick another filter, or show all of them.'
                : 'File a request for anything the organization spends outside an event.'
            }
            action={
              filtered ? (
                <Button variant="secondary" onClick={() => setStatus('all')}>
                  Show all
                </Button>
              ) : canRequest ? (
                <Button onClick={() => setFiling(true)}>New request</Button>
              ) : undefined
            }
          />
        </Card>
      ) : (
        <Card>
          <CardBody className="p-0">
            <Table minWidth={720}>
              <THead>
                <Tr>
                  <Th>What for</Th>
                  <Th>Category</Th>
                  <Th>Filed by</Th>
                  <Th>Needed by</Th>
                  <Th>Amount</Th>
                  <Th>Status</Th>
                  <Th>
                    <span className="sr-only">Actions</span>
                  </Th>
                </Tr>
              </THead>
              <TBody>
                {query.data.items.map((request) => (
                  <Tr key={request.id}>
                    <Td>{request.title}</Td>
                    <Td>{request.categoryName}</Td>
                    <Td>
                      {request.requestedBy ?? '—'}
                      <span className="block text-xs text-gray-500 dark:text-gray-400">
                        {formatDate(request.requestedAt)}
                      </span>
                    </Td>
                    <Td>{request.neededBy ? formatDate(request.neededBy) : '—'}</Td>
                    <Td numeric>{formatPeso(request.amount)}</Td>
                    <Td>
                      <RequestStatusBadge status={request.status} />
                    </Td>
                    <Td>
                      <div className="flex justify-end">
                        <Button variant="ghost" size="sm" onClick={() => setSelected(request)}>
                          Open
                        </Button>
                      </div>
                    </Td>
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

      <RequestDialog open={filing} onClose={() => setFiling(false)} />
      <RequestDrawer
        request={current}
        accounts={accounts}
        open={current !== null}
        onClose={() => setSelected(null)}
      />
    </div>
  )
}
