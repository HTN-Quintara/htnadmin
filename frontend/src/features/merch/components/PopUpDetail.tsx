import { useState } from 'react'
import { Link } from 'react-router'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { errorMessage } from '@/app/errors'
import { Alert, type AlertTone } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { buttonClass } from '@/components/ui/button-styles'
import { formatDate, formatPeso } from '@/lib/format'
import { useClosePopUpMutation, useOpenPopUpMutation } from '../api'
import type { PopUp } from '../schemas'
import { AllocationDialog } from './AllocationDialog'
import { CloseAndCountDialog } from './CloseAndCountDialog'
import { discountModeLabel, popUpStatusBadgeColor, popUpStatusLabel } from './labels'
import { WriteOffDialog } from './WriteOffDialog'

function statusNote(p: PopUp): { tone: AlertTone; text: string } {
  switch (p.status) {
    case 'Planned':
      return { tone: 'gray', text: 'Not open yet. Set the stock to bring, then open the POS when the pop-up starts.' }
    case 'Open':
      return { tone: 'green', text: 'The POS is open. Sales post to the Organization fund.' }
    case 'NeedsCount':
      return { tone: 'yellow', text: 'The store is closed. Count the returned stock and the cash drawer to finish.' }
    case 'ClosedBalanced':
      return { tone: 'blue', text: 'Closed. Every item brought was sold or returned — nothing missing.' }
    case 'ClosedMissing': {
      const missing = p.allocations.reduce((n, a) => n + (a.qtyMissing ?? 0), 0)
      // missingValue is board-only (stripped server-side for a store-team cashier) — omit the amount
      // rather than defaulting to ₱0, which would misstate it as nothing missing.
      const amount = p.missingValue != null ? ` (${formatPeso(p.missingValue)})` : ''
      if (p.isWrittenOff) {
        return {
          tone: 'red',
          text: `Closed with ${missing} item${missing === 1 ? '' : 's'} missing${amount}. Written off${p.writtenOffAt ? ` on ${formatDate(p.writtenOffAt)}` : ''}.`,
        }
      }
      return {
        tone: 'red',
        text: `Closed with ${missing} item${missing === 1 ? '' : 's'} missing${amount}. Needs a CFO write-off.`,
      }
    }
  }
}

/** Right column of the Pop-up stores tab (canvas PopUps.dc.html): header actions, line items, money, status note. */
export function PopUpDetail({ popUp }: { popUp: PopUp }) {
  const canWriteOff = useCan(Policies.FinancePost)
  const open = useOpenPopUpMutation(popUp.id)
  const close = useClosePopUpMutation(popUp.id)
  const [allocating, setAllocating] = useState(false)
  const [counting, setCounting] = useState(false)
  const [writingOff, setWritingOff] = useState(false)

  const note = statusNote(popUp)
  const posPath = `/pop-ups/${encodeURIComponent(popUp.id)}/pos`

  function onCloseStore() {
    close.mutate(undefined, { onSuccess: () => setCounting(true) })
  }

  return (
    <Card className="flex flex-col gap-0">
      <CardHeader
        title={popUp.name}
        description={`${popUp.ownerCommitteeName} · ${discountModeLabel(popUp.discountMode)} discounts · ${formatPeso(popUp.feeAmount)} fee`}
        action={
          <div className="flex flex-wrap items-center gap-2">
            <Badge color={popUpStatusBadgeColor(popUp.status)}>{popUpStatusLabel(popUp.status)}</Badge>
            {popUp.status === 'Planned' && popUp.canManage ? (
              <Button size="sm" variant="secondary" onClick={() => setAllocating(true)}>
                Edit stock to bring
              </Button>
            ) : null}
            {popUp.status === 'Planned' && popUp.canManage ? (
              <Button size="sm" loading={open.isPending} onClick={() => open.mutate()}>
                Open
              </Button>
            ) : null}
            {popUp.status === 'Open' && popUp.canSell ? (
              <Link to={posPath} className={buttonClass('secondary', 'sm')}>
                Open POS
              </Link>
            ) : null}
            {popUp.status === 'Open' && popUp.canManage ? (
              <Button size="sm" loading={close.isPending} onClick={onCloseStore}>
                Close store &amp; count
              </Button>
            ) : null}
            {popUp.status === 'NeedsCount' && popUp.canManage ? (
              <Button size="sm" onClick={() => setCounting(true)}>
                Count stock
              </Button>
            ) : null}
            {popUp.status === 'ClosedMissing' && !popUp.isWrittenOff && canWriteOff ? (
              <Button size="sm" variant="danger" onClick={() => setWritingOff(true)}>
                Write off
              </Button>
            ) : null}
          </div>
        }
      />

      <div className="flex flex-col gap-4 p-5">
        {open.isError ? <Alert tone="red" live="alert">{errorMessage(open.error)}</Alert> : null}
        {close.isError ? <Alert tone="red" live="alert">{errorMessage(close.error)}</Alert> : null}

        <p className="text-sm text-gray-600 dark:text-gray-400">
          {formatDate(popUp.startsAt)} – {formatDate(popUp.endsAt)} · {popUp.venueOrgName ?? popUp.venueText ?? 'No venue set'}
        </p>

        <div>
          <h3 className="mb-2 text-sm font-semibold text-gray-900 dark:text-gray-50">Store team</h3>
          {popUp.team.length === 0 ? (
            <p className="text-sm text-gray-600 dark:text-gray-400">Nobody assigned yet.</p>
          ) : (
            <ul className="flex flex-wrap gap-1.5">
              {popUp.team.map((m) => (
                <li key={m.personId}>
                  <Badge color="gray">{m.name}</Badge>
                </li>
              ))}
            </ul>
          )}
        </div>

        <Table minWidth={560}>
          <THead>
            <Tr>
              <Th>Item</Th>
              <Th className="text-right">Brought</Th>
              <Th className="text-right">Sold</Th>
              <Th className="text-right">Returned</Th>
              <Th className="text-right">Missing</Th>
            </Tr>
          </THead>
          <TBody>
            {popUp.allocations.length === 0 ? (
              <Tr>
                <Td colSpan={5} className="text-center text-gray-600 dark:text-gray-400">
                  No stock allocated yet.
                </Td>
              </Tr>
            ) : (
              popUp.allocations.map((a) => (
                <Tr key={a.variantId}>
                  <Td className="font-medium text-gray-900 dark:text-gray-50">
                    {a.itemName} ({a.variantLabel})
                  </Td>
                  <Td numeric>{a.qtyBrought}</Td>
                  <Td numeric>{a.qtySold}</Td>
                  <Td numeric>{a.qtyReturned ?? '—'}</Td>
                  <Td numeric className={a.qtyMissing ? 'font-semibold text-red-700 dark:text-red-400' : undefined}>
                    {a.qtyMissing ?? '—'}
                  </Td>
                </Tr>
              ))
            )}
          </TBody>
        </Table>

        <div className="grid grid-cols-2 gap-3 rounded-lg bg-gray-50 p-4 text-sm sm:grid-cols-4 dark:bg-white/5">
          <SummaryStat label="Sales total" value={popUp.totalSales != null ? formatPeso(popUp.totalSales) : '—'} />
          <SummaryStat label="Discounts given" value={popUp.discountsGiven != null ? formatPeso(popUp.discountsGiven) : '—'} />
          <SummaryStat label="Cash counted" value={popUp.cashCounted != null ? formatPeso(popUp.cashCounted) : '—'} />
          <SummaryStat label="Missing value" value={popUp.missingValue != null ? formatPeso(popUp.missingValue) : '—'} />
        </div>

        <Alert tone={note.tone}>{note.text}</Alert>
      </div>

      {allocating ? <AllocationDialog popUp={popUp} onClose={() => setAllocating(false)} /> : null}
      {counting ? <CloseAndCountDialog popUp={popUp} onClose={() => setCounting(false)} /> : null}
      {writingOff ? <WriteOffDialog popUp={popUp} onClose={() => setWritingOff(false)} /> : null}
    </Card>
  )
}

function SummaryStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-0.5">
      <span className="text-xs text-gray-600 dark:text-gray-400">{label}</span>
      <span className="font-semibold tabular-nums text-gray-900 dark:text-gray-50">{value}</span>
    </div>
  )
}
