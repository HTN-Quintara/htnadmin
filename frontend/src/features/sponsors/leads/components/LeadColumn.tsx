import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Select, Textarea } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useConfirmLeadMutation, useLeadStatusMutation } from '../api'
import { leadStatuses, type LeadCard, type LeadStatus } from '../schemas'

const statusNames: Record<LeadStatus, string> = {
  Prospect: 'Prospect', Contacted: 'Contacted', InTalks: 'In talks', Confirmed: 'Confirmed', Declined: 'Declined',
}

export function LeadColumn({ eventId, column }: { eventId: string; column: { status: LeadStatus; name: string; count: number; cards: LeadCard[] } }) {
  return (
    <section aria-label={column.name} className="flex w-56 shrink-0 flex-col gap-2 rounded-xl bg-gray-100 p-3 dark:bg-white/5">
      <div className="flex items-center gap-2 px-1">
        <h2 className="flex-1 text-[0.8125rem] font-semibold text-gray-900 dark:text-gray-50">{column.name}</h2>
        <span className="text-xs font-semibold text-gray-500 dark:text-gray-400">{column.count}</span>
      </div>
      {column.cards.map((card) => <LeadCardView key={card.id} eventId={eventId} card={card} />)}
    </section>
  )
}

function LeadCardView({ eventId, card }: { eventId: string; card: LeadCard }) {
  const move = useLeadStatusMutation(eventId)
  const confirm = useConfirmLeadMutation(eventId)
  const { toast } = useToast()
  const [declineOpen, setDeclineOpen] = useState(false)

  function onMove(status: string) {
    if (status === card.status) return
    if (status === 'Declined') { setDeclineOpen(true); return }
    if (status === 'Confirmed') {
      confirm.mutate(card.id, { onError: (e) => toast({ tone: 'error', title: errorMessage(e) }) })
      return
    }
    move.mutate({ id: card.id, status }, { onError: (e) => toast({ tone: 'error', title: errorMessage(e) }) })
  }

  return (
    <article className="flex flex-col gap-2 rounded-md bg-white p-3 shadow-sm ring-1 ring-gray-900/5 dark:bg-gray-900 dark:ring-white/10">
      <div className="flex flex-col">
        <span className="text-sm font-semibold text-gray-900 dark:text-gray-50">{card.subject}</span>
        {card.contactName && <span className="text-xs text-gray-500 dark:text-gray-400">{[card.contactName, card.contactRole].filter(Boolean).join(' · ')}</span>}
      </div>
      {card.ask && <span className="text-xs text-gray-600 dark:text-gray-300">{card.ask}</span>}
      <div className="flex items-center gap-2 text-xs">
        {card.ownerInitials && (
          <span aria-hidden className="flex h-5 w-5 items-center justify-center rounded-full bg-brand-50 text-[0.625rem] font-semibold text-brand-700 dark:bg-brand-300/10 dark:text-brand-300">{card.ownerInitials}</span>
        )}
        {card.nextStep && (
          <span className={card.overdue ? 'font-semibold text-red-700 dark:text-red-400' : 'text-gray-500 dark:text-gray-400'}>
            {card.nextStep}{card.nextStepDue ? ` · ${card.nextStepDue}` : ''}
          </span>
        )}
        {card.fromDirectory && <Badge color="gray">Directory</Badge>}
      </div>

      {card.status === 'Confirmed' ? (
        <ConfirmedCta card={card} />
      ) : (
        <Select
          controlSize="md"
          value={card.status}
          disabled={move.isPending || confirm.isPending}
          onChange={(e) => onMove(e.target.value)}
          aria-label={`Move ${card.subject} to`}
        >
          {leadStatuses.map((s) => <option key={s} value={s}>{statusNames[s]}</option>)}
        </Select>
      )}

      <DeclineDialog
        open={declineOpen}
        onClose={() => setDeclineOpen(false)}
        pending={move.isPending}
        onConfirm={(reason) => move.mutate({ id: card.id, status: 'Declined', reason }, {
          onSuccess: () => setDeclineOpen(false),
          onError: (e) => toast({ tone: 'error', title: errorMessage(e) }),
        })}
      />
    </article>
  )
}

function ConfirmedCta({ card }: { card: LeadCard }) {
  if (card.kind === 'Sponsor') {
    return <a href="../sponsors" className="rounded-md border border-green-200 bg-green-50 py-1.5 text-center text-xs font-semibold text-green-700 dark:border-green-900 dark:bg-green-950/40 dark:text-green-400">Open sponsor entry</a>
  }
  return <span className="rounded-md border border-green-200 bg-green-50 py-1.5 text-center text-xs font-semibold text-green-700 dark:border-green-900 dark:bg-green-950/40 dark:text-green-400">Send the link from Speakers</span>
}

function DeclineDialog({ open, onClose, onConfirm, pending }: { open: boolean; onClose: () => void; onConfirm: (reason: string) => void; pending: boolean }) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  return (
    <Dialog open={open} onClose={onClose} role="alertdialog" title="Decline lead"
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button variant="destructive" loading={pending} onClick={() => { if (!reason.trim()) { setError('Give a reason.'); return } onConfirm(reason.trim()) }}>Decline</Button></>}>
      <Field label="Reason" error={error ?? undefined}>
        {(c) => <Textarea {...c} rows={3} value={reason} onChange={(e) => { setReason(e.target.value); setError(null) }} />}
      </Field>
    </Dialog>
  )
}
