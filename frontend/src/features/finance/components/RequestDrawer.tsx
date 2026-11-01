import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Drawer } from '@/components/ui/Drawer'
import { Field } from '@/components/ui/Field'
import { FileButton } from '@/components/ui/FileButton'
import { Input, Select } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { formatDate, formatPeso } from '@/lib/format'
import { useCancelRequestMutation, useDecideRequestMutation, usePayRequestMutation } from '../api'
import type { ExpenseRequest } from '../schemas'
import { RequestStatusBadge } from './badges'
import { ReasonDialog } from './ReasonDialog'

/**
 * One request, with whatever the caller may actually do to it. The CFO approves, rejects with a reason, then marks it
 * paid with the receipt for the money that left; the requester can withdraw their own while it is still pending.
 */
export function RequestDrawer({
  request,
  accounts,
  open,
  onClose,
}: {
  request: ExpenseRequest | null
  accounts: { id: string; label: string; isActive: boolean }[]
  open: boolean
  onClose: () => void
}) {
  const { toast } = useToast()
  const decide = useDecideRequestMutation()
  const pay = usePayRequestMutation()
  const cancel = useCancelRequestMutation()
  const [rejecting, setRejecting] = useState(false)
  const [cancelling, setCancelling] = useState(false)
  const [accountId, setAccountId] = useState('')
  const [entryDate, setEntryDate] = useState(today())
  const [receipt, setReceipt] = useState<File | null>(null)
  const [payError, setPayError] = useState<string | null>(null)

  if (!request) return null

  const onApprove = () => {
    if (decide.isPending) return
    decide.mutate(
      { id: request.id, approve: true },
      {
        onSuccess: () => toast({ tone: 'success', title: 'Request approved.' }),
        onError: (e) => toast({ tone: 'error', title: 'Could not approve it.', description: errorMessage(e) }),
      },
    )
  }

  const onPay = () => {
    if (pay.isPending) return
    if (!accountId) {
      setPayError('Choose the account the money came out of.')
      return
    }
    if (!receipt) {
      setPayError('Attach the receipt for the payment.')
      return
    }
    setPayError(null)
    pay.mutate(
      { id: request.id, paymentAccountId: accountId, entryDate, receipt },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: 'Marked paid and posted to the Organization fund.' })
          setReceipt(null)
        },
        onError: (e) => toast({ tone: 'error', title: 'Could not mark it paid.', description: errorMessage(e) }),
      },
    )
  }

  return (
    <>
      <Drawer
        open={open}
        onClose={onClose}
        label={`Expense request: ${request.title}`}
        footer={
          <div className="flex flex-wrap items-center justify-end gap-2">
            {request.canCancel ? (
              <Button variant="secondary" onClick={() => setCancelling(true)}>
                Withdraw
              </Button>
            ) : null}
            {request.canReject ? (
              <Button variant="secondary" onClick={() => setRejecting(true)}>
                Reject…
              </Button>
            ) : null}
            {request.canApprove ? (
              <Button onClick={onApprove} loading={decide.isPending}>
                Approve
              </Button>
            ) : null}
          </div>
        }
      >
        <div className="flex flex-col gap-5">
          <div className="flex flex-col gap-2">
            <div className="flex items-center gap-2">
              <RequestStatusBadge status={request.status} />
              <span className="text-sm text-gray-500 dark:text-gray-400">{request.categoryName}</span>
            </div>
            <h3 className="text-base font-semibold text-gray-900 dark:text-gray-100">{request.title}</h3>
            <p className="text-2xl font-semibold tabular-nums text-gray-900 dark:text-gray-100">
              {formatPeso(request.amount)}
            </p>
            {request.description ? (
              <p className="text-sm whitespace-pre-wrap text-gray-700 dark:text-gray-300">{request.description}</p>
            ) : null}
          </div>

          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
            <dt className="text-gray-500 dark:text-gray-400">Filed by</dt>
            <dd className="text-gray-900 dark:text-gray-100">
              {request.requestedBy ?? '—'} · {formatDate(request.requestedAt)}
            </dd>
            {request.neededBy ? (
              <>
                <dt className="text-gray-500 dark:text-gray-400">Needed by</dt>
                <dd className="text-gray-900 dark:text-gray-100">{formatDate(request.neededBy)}</dd>
              </>
            ) : null}
            {request.decidedAt ? (
              <>
                <dt className="text-gray-500 dark:text-gray-400">Decided</dt>
                <dd className="text-gray-900 dark:text-gray-100">
                  {request.decidedBy ?? 'the CFO'} · {formatDate(request.decidedAt)}
                </dd>
              </>
            ) : null}
            {request.decisionReason ? (
              <>
                <dt className="text-gray-500 dark:text-gray-400">
                  {request.status === 'Rejected' ? 'Reason' : 'Note'}
                </dt>
                <dd className="text-gray-900 dark:text-gray-100">{request.decisionReason}</dd>
              </>
            ) : null}
            {request.paidAt ? (
              <>
                <dt className="text-gray-500 dark:text-gray-400">Paid</dt>
                <dd className="text-gray-900 dark:text-gray-100">{formatDate(request.paidAt)}</dd>
              </>
            ) : null}
            {request.cancelReason ? (
              <>
                <dt className="text-gray-500 dark:text-gray-400">Withdrawn</dt>
                <dd className="text-gray-900 dark:text-gray-100">{request.cancelReason}</dd>
              </>
            ) : null}
          </dl>

          {request.canPay ? (
            <div className="flex flex-col gap-3 rounded-lg ring-1 ring-gray-900/5 p-4 dark:ring-white/10">
              <h4 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Mark it paid</h4>
              <p className="text-sm text-gray-600 dark:text-gray-400">
                This posts {formatPeso(request.amount)} out of the Organization fund.
              </p>
              <Field label="Account the money left" error={payError ?? undefined}>
                {(p) => (
                  <Select {...p} value={accountId} onChange={(e) => setAccountId(e.target.value)}>
                    <option value="">Choose an account</option>
                    {accounts
                      .filter((a) => a.isActive)
                      .map((a) => (
                        <option key={a.id} value={a.id}>
                          {a.label}
                        </option>
                      ))}
                  </Select>
                )}
              </Field>
              <Field label="Date it left">
                {(p) => (
                  <Input {...p} type="date" value={entryDate} onChange={(e) => setEntryDate(e.target.value)} />
                )}
              </Field>
              <div className="flex flex-wrap items-center gap-3">
                <FileButton accept="image/png,image/jpeg,application/pdf" onFile={setReceipt}>
                  {receipt ? 'Choose another file' : 'Attach the receipt'}
                </FileButton>
                {receipt ? <span className="text-sm text-gray-600 dark:text-gray-400">{receipt.name}</span> : null}
              </div>
              <Button onClick={onPay} loading={pay.isPending} block>
                Mark paid
              </Button>
            </div>
          ) : null}
        </div>
      </Drawer>

      <ReasonDialog
        open={rejecting}
        onClose={() => setRejecting(false)}
        title="Reject this request"
        description="The requester is emailed your reason."
        label="Why are you rejecting it?"
        confirmLabel="Reject request"
        pending={decide.isPending}
        onConfirm={(reason) => {
          if (decide.isPending) return
          decide.mutate(
            { id: request.id, approve: false, reason },
            {
              onSuccess: () => {
                toast({ tone: 'success', title: 'Request rejected.' })
                setRejecting(false)
              },
              onError: (e) => toast({ tone: 'error', title: 'Could not reject it.', description: errorMessage(e) }),
            },
          )
        }}
      />

      <ReasonDialog
        open={cancelling}
        onClose={() => setCancelling(false)}
        title="Withdraw this request"
        label="Why are you withdrawing it?"
        confirmLabel="Withdraw"
        pending={cancel.isPending}
        onConfirm={(reason) => {
          if (cancel.isPending) return
          cancel.mutate(
            { id: request.id, reason },
            {
              onSuccess: () => {
                toast({ tone: 'success', title: 'Request withdrawn.' })
                setCancelling(false)
              },
              onError: (e) => toast({ tone: 'error', title: 'Could not withdraw it.', description: errorMessage(e) }),
            },
          )
        }}
      />
    </>
  )
}

function today(): string {
  return new Date(Date.now() + 8 * 60 * 60 * 1000).toISOString().slice(0, 10)
}
