import { useRef, useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Drawer } from '@/components/ui/Drawer'
import { Field } from '@/components/ui/Field'
import { Input, Select, Textarea, Checkbox } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import { formatPeso } from '@/lib/format'
import { usePaymentAccountsQuery } from '@/features/settings/api'
import {
  useCashReceivedMutation,
  useSponsorActionMutation,
  useSponsorDetailQuery,
  useUploadSponsorLogoMutation,
} from '../api'
import type { SponsorTier } from '../schemas'
import { SponsorStatusBadge } from './badges'
import { SponsorDialog } from './SponsorDialog'

type Props = { eventId: string; sponsorId: string | null; tiers: SponsorTier[]; onClose: () => void }

export function SponsorDrawer({ eventId, sponsorId, tiers, onClose }: Props) {
  const query = useSponsorDetailQuery(eventId, sponsorId)
  const action = useSponsorActionMutation(eventId)
  const { toast } = useToast()
  const [edit, setEdit] = useState(false)
  const [cash, setCash] = useState(false)
  const [reason, setReason] = useState<null | 'unconfirm' | 'cash-received/reverse' | 'void'>(null)

  const open = sponsorId !== null
  const sponsor = query.data?.sponsor
  const perks = query.data?.perks ?? []

  function run(path: string, body?: Record<string, unknown>, done?: () => void) {
    if (!sponsorId || action.isPending) return
    action.mutate(
      { sid: sponsorId, action: path, body },
      {
        onSuccess: () => { toast({ tone: 'success', title: 'Updated.' }); done?.() },
        onError: (e) => toast({ tone: 'error', title: errorMessage(e) }),
      },
    )
  }

  return (
    <Drawer open={open} onClose={onClose} label="Sponsor details">
      {query.isPending || !sponsor ? (
        <div className="flex flex-col gap-3" role="status" aria-label="Loading sponsor">
          <Skeleton className="h-6 w-48" />
          <Skeleton className="h-40 w-full" />
        </div>
      ) : (
        <div className="flex flex-col gap-5">
          <div className="flex items-start justify-between gap-3">
            <div>
              <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-50">
                {sponsor.isAnonymous ? 'Anonymous donor' : sponsor.name}
              </h2>
              <div className="mt-1"><SponsorStatusBadge status={sponsor.status} /></div>
            </div>
            <Button size="sm" variant="secondary" onClick={() => setEdit(true)}>Edit</Button>
          </div>

          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
            <dt className="text-gray-500 dark:text-gray-400">Contribution</dt><dd>{sponsor.amountText}</dd>
            {sponsor.tierName && (<><dt className="text-gray-500 dark:text-gray-400">Tier</dt><dd>{sponsor.tierClass} · {sponsor.tierName}</dd></>)}
            <dt className="text-gray-500 dark:text-gray-400">Comped</dt>
            <dd>{sponsor.compsEntitlement > 0 ? `${sponsor.compsUsed} / ${sponsor.compsEntitlement}` : 'None'}</dd>
            {sponsor.cashReceived && (<><dt className="text-gray-500 dark:text-gray-400">Cash received</dt><dd>Posted to the ledger{sponsor.cashReference ? ` · ${sponsor.cashReference}` : ''}</dd></>)}
          </dl>

          <div className="flex flex-wrap gap-2">
            {sponsor.status === 'AwaitingContract' && (
              <Button size="sm" loading={action.isPending} onClick={() => run('confirm')}>Confirm</Button>
            )}
            {sponsor.status === 'Confirmed' && !sponsor.cashReceived && (
              <Button size="sm" variant="secondary" onClick={() => setReason('unconfirm')}>Un-confirm</Button>
            )}
            {sponsor.status === 'Confirmed' && sponsor.cashAmount != null && !sponsor.cashReceived && (
              <Button size="sm" onClick={() => setCash(true)}>Record cash · {formatPeso(sponsor.cashAmount)}</Button>
            )}
            {sponsor.cashReceived && (
              <Button size="sm" variant="secondary" onClick={() => setReason('cash-received/reverse')}>Reverse cash</Button>
            )}
            {sponsor.kind === 'Organization' && <LogoButton eventId={eventId} sponsorId={sponsor.id} />}
            {!sponsor.cashReceived && (
              <Button size="sm" variant="destructive" onClick={() => setReason('void')}>Remove</Button>
            )}
          </div>

          <PerksSection eventId={eventId} sponsorId={sponsor.id} perks={perks} />

          <SponsorDialog eventId={eventId} tiers={tiers} open={edit} onClose={() => setEdit(false)} sponsor={sponsor} />
          <CashDialog eventId={eventId} sponsorId={sponsor.id} open={cash} onClose={() => setCash(false)} />
          <ReasonDialog
            open={reason !== null}
            title={reason === 'void' ? 'Remove sponsor' : reason === 'unconfirm' ? 'Un-confirm sponsor' : 'Reverse cash'}
            onClose={() => setReason(null)}
            onConfirm={(r) => { const act = reason; if (!act) return; run(act, { reason: r }, () => { setReason(null); if (act === 'void') onClose() }) }}
            pending={action.isPending}
          />
        </div>
      )}
    </Drawer>
  )
}

function LogoButton({ eventId, sponsorId }: { eventId: string; sponsorId: string }) {
  const upload = useUploadSponsorLogoMutation(eventId)
  const { toast } = useToast()
  const ref = useRef<HTMLInputElement>(null)
  return (
    <>
      <Button size="sm" variant="secondary" loading={upload.isPending} onClick={() => ref.current?.click()}>Upload logo</Button>
      <input
        ref={ref}
        type="file"
        accept="image/png"
        className="hidden"
        onChange={(e) => {
          const file = e.target.files?.[0]
          e.target.value = ''
          if (!file) return
          upload.mutate(
            { sid: sponsorId, file },
            { onSuccess: () => toast({ tone: 'success', title: 'Logo uploaded.' }), onError: (err) => toast({ tone: 'error', title: errorMessage(err) }) },
          )
        }}
      />
    </>
  )
}

function PerksSection({ eventId, sponsorId, perks }: { eventId: string; sponsorId: string; perks: { id: string; text: string; delivered: boolean; removed: boolean }[] }) {
  const action = useSponsorActionMutation(eventId)
  const live = perks.filter((p) => !p.removed)
  if (live.length === 0) return null
  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-50">Perks</h3>
      <ul className="flex flex-col gap-1">
        {live.map((p) => (
          <li key={p.id} className="flex items-center gap-2 text-sm">
            <Checkbox
              checked={p.delivered}
              aria-label={`${p.text} delivered`}
              onChange={(e) => action.mutate({ sid: sponsorId, action: `perks/${p.id}/delivered`, body: { delivered: e.target.checked } })}
            />
            <span className={p.delivered ? 'text-gray-500 line-through dark:text-gray-400' : ''}>{p.text}</span>
          </li>
        ))}
      </ul>
    </section>
  )
}

function CashDialog({ eventId, sponsorId, open, onClose }: { eventId: string; sponsorId: string; open: boolean; onClose: () => void }) {
  const accounts = usePaymentAccountsQuery(false, open)
  const cash = useCashReceivedMutation(eventId)
  const { toast } = useToast()
  const [accountId, setAccountId] = useState('')
  const [reference, setReference] = useState('')
  const today = new Date().toISOString().slice(0, 10)
  const [receivedOn, setReceivedOn] = useState(today)
  const [error, setError] = useState<string | null>(null)

  function submit() {
    if (cash.isPending) return
    if (!accountId) { setError('Pick the account the money landed in.'); return }
    cash.mutate(
      { sid: sponsorId, body: { paymentAccountId: accountId, receivedOn, reference: reference || null } },
      {
        onSuccess: () => { toast({ tone: 'success', title: 'Cash recorded and posted to the ledger.' }); onClose() },
        onError: (e) => setError(errorMessage(e)),
      },
    )
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Record cash received"
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button loading={cash.isPending} onClick={submit}>Record</Button></>}
    >
      <div className="flex flex-col gap-4">
        {error && <Alert tone="red">{error}</Alert>}
        <Field label="Account">
          {(c) => (
            <Select {...c} value={accountId} onChange={(e) => setAccountId(e.target.value)}>
              <option value="">Select an account</option>
              {(accounts.data ?? []).map((a) => <option key={a.id} value={a.id}>{a.label}</option>)}
            </Select>
          )}
        </Field>
        <Field label="Received on">{(c) => <Input type="date" {...c} value={receivedOn} onChange={(e) => setReceivedOn(e.target.value)} />}</Field>
        <Field label="Reference" optional>{(c) => <Input {...c} value={reference} onChange={(e) => setReference(e.target.value)} placeholder="OR or bank reference" />}</Field>
      </div>
    </Dialog>
  )
}

function ReasonDialog({ open, title, onClose, onConfirm, pending }: { open: boolean; title: string; onClose: () => void; onConfirm: (reason: string) => void; pending: boolean }) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  return (
    <Dialog
      open={open}
      onClose={onClose}
      role="alertdialog"
      title={title}
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button variant="destructive" loading={pending} onClick={() => { if (!reason.trim()) { setError('Give a reason.'); return } onConfirm(reason.trim()) }}>{title}</Button></>}
    >
      <Field label="Reason" error={error ?? undefined}>
        {(c) => <Textarea {...c} rows={3} value={reason} onChange={(e) => { setReason(e.target.value); setError(null) }} />}
      </Field>
    </Dialog>
  )
}
