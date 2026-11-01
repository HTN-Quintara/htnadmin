import { useState } from 'react'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardBody } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { FilterChip } from '@/components/ui/FilterChip'
import { Input, Select } from '@/components/ui/Input'
import { Menu } from '@/components/ui/Menu'
import { Pagination } from '@/components/ui/Pagination'
import { Skeleton } from '@/components/ui/Spinner'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { WalletIcon } from '@/components/ui/icons'
import { buttonClass } from '@/components/ui/button-styles'
import { useToast } from '@/components/ui/toast-context'
import { formatDate, formatPeso, formatPesoSigned } from '@/lib/format'
import {
  exportLedgerCsvUrl,
  receiptUrl,
  useApproveEntryMutation,
  useLedgerQuery,
  useVoidEntryMutation,
  type LedgerFilter,
} from '../api'
import type { LedgerStatus } from '../schemas'
import { DirectionBadge, LedgerStatusBadge } from './badges'
import { ReasonDialog } from './ReasonDialog'
import { LedgerEntryDialog } from './LedgerEntryDialog'
import { TransferDialog } from './TransferDialog'

const statusChips: { value: LedgerStatus | 'all'; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'Pending', label: 'Waiting' },
  { value: 'Posted', label: 'Posted' },
  { value: 'Voided', label: 'Voided' },
]

/** Frame 24b: the entries, with the actions the caller is actually allowed to take on each row. */
export function LedgerView({
  canPost,
  canCreate,
  isCfo,
  accounts,
}: {
  canPost: boolean
  canCreate: boolean
  isCfo: boolean
  accounts: { id: string; label: string; isActive: boolean }[]
}) {
  const { toast } = useToast()
  const [status, setStatus] = useState<LedgerStatus | 'all'>('all')
  const [search, setSearch] = useState('')
  const [accountId, setAccountId] = useState('')
  const [page, setPage] = useState(1)
  const [newEntryOpen, setNewEntryOpen] = useState(false)
  const [transferOpen, setTransferOpen] = useState(false)
  const [voiding, setVoiding] = useState<string | null>(null)

  const filter: LedgerFilter = {
    status: status === 'all' ? undefined : status,
    search: search.trim() || undefined,
    accountId: accountId || undefined,
    page,
  }
  const query = useLedgerQuery(filter)
  const approve = useApproveEntryMutation()
  const voidEntry = useVoidEntryMutation()
  const filtered = status !== 'all' || !!search.trim() || !!accountId

  const onApprove = (id: string) => {
    if (approve.isPending) return
    approve.mutate(id, {
      onSuccess: () => toast({ tone: 'success', title: 'Entry approved and posted.' }),
      onError: (e) => toast({ tone: 'error', title: 'Could not approve the entry.', description: errorMessage(e) }),
    })
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          {statusChips.map((chip) => (
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
        <div className="flex flex-wrap items-center gap-2">
          <Input
            type="search"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              setPage(1)
            }}
            placeholder="Search description or reference"
            aria-label="Search the ledger"
            className="w-56"
          />
          <Select
            value={accountId}
            onChange={(e) => {
              setAccountId(e.target.value)
              setPage(1)
            }}
            aria-label="Filter by account"
          >
            <option value="">All accounts</option>
            {accounts.map((a) => (
              <option key={a.id} value={a.id}>
                {a.label}
              </option>
            ))}
          </Select>
          {canPost ? (
            <a className={buttonClass('secondary')} href={exportLedgerCsvUrl(filter)}>
              Export CSV
            </a>
          ) : null}
          {canPost ? (
            <Button variant="secondary" onClick={() => setTransferOpen(true)}>
              Transfer
            </Button>
          ) : null}
          {canCreate ? <Button onClick={() => setNewEntryOpen(true)}>New entry</Button> : null}
        </div>
      </div>

      {query.data ? (
        <div className="flex flex-wrap gap-x-6 gap-y-1 text-sm text-gray-600 tabular-nums dark:text-gray-400">
          <span>
            In <strong className="text-gray-900 dark:text-gray-100">{formatPeso(query.data.totals.moneyIn)}</strong>
          </span>
          <span>
            Out <strong className="text-gray-900 dark:text-gray-100">{formatPeso(query.data.totals.moneyOut)}</strong>
          </span>
          <span>
            Net <strong className="text-gray-900 dark:text-gray-100">{formatPesoSigned(query.data.totals.net)}</strong>
          </span>
          {query.data.totals.pendingCount > 0 ? (
            <span>
              Waiting for approval{' '}
              <strong className="text-gray-900 dark:text-gray-100">{query.data.totals.pendingCount}</strong>
            </span>
          ) : null}
        </div>
      ) : null}

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
          Could not load the ledger. {errorMessage(query.error)}
        </Alert>
      ) : query.isPending ? (
        <div className="flex flex-col gap-2" role="status" aria-label="Loading the ledger…">
          {[0, 1, 2, 3, 4].map((i) => (
            <Skeleton key={i} className="h-12" />
          ))}
        </div>
      ) : query.data.items.length === 0 ? (
        <Card>
          <EmptyState
            icon={<WalletIcon size={22} />}
            title={filtered ? 'No entries match these filters' : 'No entries yet'}
            description={
              filtered
                ? 'Clear the filters to see the whole ledger.'
                : canCreate
                  ? 'Post the first entry, or mark a registration paid and it will appear here.'
                  : 'Entries appear here as money comes in and goes out.'
            }
            action={
              filtered ? (
                <Button
                  variant="secondary"
                  onClick={() => {
                    setStatus('all')
                    setSearch('')
                    setAccountId('')
                    setPage(1)
                  }}
                >
                  Clear filters
                </Button>
              ) : canCreate ? (
                <Button onClick={() => setNewEntryOpen(true)}>New entry</Button>
              ) : undefined
            }
          />
        </Card>
      ) : (
        <Card>
          <CardBody className="p-0">
            <Table minWidth={900}>
              <THead>
                <Tr>
                  <Th>Date</Th>
                  <Th>Description</Th>
                  <Th>Fund</Th>
                  <Th>Category</Th>
                  <Th>Account</Th>
                  <Th>In / out</Th>
                  <Th>Amount</Th>
                  <Th>Status</Th>
                  <Th>
                    <span className="sr-only">Actions</span>
                  </Th>
                </Tr>
              </THead>
              <TBody>
                {query.data.items.map((entry) => {
                  const actions = [
                    ...(entry.canApprove ? [{ label: 'Approve', onSelect: () => onApprove(entry.id) }] : []),
                    ...(entry.canVoid ? [{ label: 'Void…', onSelect: () => setVoiding(entry.id) }] : []),
                  ]
                  return (
                    <Tr key={entry.id}>
                      <Td>{formatDate(entry.entryDate)}</Td>
                      <Td>
                        <div className="flex flex-col">
                          <span>{entry.description}</span>
                          <span className="text-xs text-gray-500 dark:text-gray-400">
                            {entry.reference ? `${entry.reference} · ` : ''}
                            {entry.enteredBy ?? 'System'}
                            {entry.isInClosedMonth ? ' · closed month' : ''}
                          </span>
                        </div>
                      </Td>
                      <Td>{entry.fundName}</Td>
                      <Td>{entry.categoryName}</Td>
                      <Td>{entry.accountLabel ?? '—'}</Td>
                      <Td>
                        <DirectionBadge direction={entry.direction} />
                      </Td>
                      <Td numeric>{formatPeso(entry.amount)}</Td>
                      <Td>
                        <LedgerStatusBadge status={entry.status} />
                      </Td>
                      <Td>
                        <div className="flex items-center justify-end gap-2">
                          {entry.hasReceipt ? (
                            <a className={buttonClass('ghost', 'sm')} href={receiptUrl(entry.id)}>
                              Receipt
                            </a>
                          ) : null}
                          {actions.length > 0 ? (
                            <Menu items={actions} aria-label={`Actions for ${entry.description}`} variant="ghost" size="sm">
                              Actions
                            </Menu>
                          ) : null}
                        </div>
                      </Td>
                    </Tr>
                  )
                })}
              </TBody>
            </Table>
          </CardBody>
        </Card>
      )}

      {query.data && query.data.total > query.data.pageSize ? (
        <Pagination page={query.data.page} pageSize={query.data.pageSize} total={query.data.total} onPage={setPage} />
      ) : null}

      <LedgerEntryDialog
        open={newEntryOpen}
        onClose={() => setNewEntryOpen(false)}
        accounts={accounts}
        isCfo={isCfo}
      />
      <TransferDialog open={transferOpen} onClose={() => setTransferOpen(false)} />
      <ReasonDialog
        open={voiding !== null}
        onClose={() => setVoiding(null)}
        title="Void this entry"
        description="The entry stays in the ledger with your reason; it stops counting towards any balance."
        label="Why are you voiding it?"
        confirmLabel="Void entry"
        pending={voidEntry.isPending}
        onConfirm={(reason) => {
          if (!voiding || voidEntry.isPending) return
          voidEntry.mutate(
            { id: voiding, reason },
            {
              onSuccess: () => {
                toast({ tone: 'success', title: 'Entry voided.' })
                setVoiding(null)
              },
              onError: (e) =>
                toast({
                  tone: 'error',
                  title: 'Could not void the entry.',
                  description: isApiError(e) ? errorMessage(e) : errorMessage(e),
                }),
            },
          )
        }}
      />
    </div>
  )
}
