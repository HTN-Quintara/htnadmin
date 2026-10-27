import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Drawer } from '@/components/ui/Drawer'
import { Field } from '@/components/ui/Field'
import { Textarea } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import { useApprovePhotoMutation, useSpeakerActionMutation, useSpeakerDetailQuery } from '../api'

export function SpeakerDrawer({ eventId, speakerId, onClose }: { eventId: string; speakerId: string | null; onClose: () => void }) {
  const query = useSpeakerDetailQuery(eventId, speakerId)
  const action = useSpeakerActionMutation(eventId)
  const approvePhoto = useApprovePhotoMutation(eventId)
  const { toast } = useToast()
  const [revokeOpen, setRevokeOpen] = useState(false)
  const [removeOpen, setRemoveOpen] = useState(false)

  const open = speakerId !== null
  const sp = query.data

  function run(action_: string, body?: Record<string, unknown>, onDone?: () => void) {
    if (!speakerId || action.isPending) return
    action.mutate({ sid: speakerId, action: action_, body }, {
      onSuccess: () => { toast({ tone: 'success', title: 'Updated.' }); onDone?.() },
      onError: (e) => toast({ tone: 'error', title: errorMessage(e) }),
    })
  }

  return (
    <Drawer open={open} onClose={onClose} label="Speaker details">
      {query.isPending || !sp ? (
        <div role="status" aria-label="Loading speaker" className="flex flex-col gap-3"><Skeleton className="h-6 w-48" /><Skeleton className="h-40 w-full" /></div>
      ) : (
        <div className="flex flex-col gap-5">
          <div>
            <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-50">{sp.name}</h2>
            <p className="text-sm text-gray-500 dark:text-gray-400">{sp.session}</p>
          </div>

          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
            <dt className="text-gray-500 dark:text-gray-400">Email</dt><dd>{sp.email}</dd>
            <dt className="text-gray-500 dark:text-gray-400">Photo</dt><dd>{sp.photoState}</dd>
            {sp.travel && (
              <>
                <dt className="text-gray-500 dark:text-gray-400">Arrival</dt><dd>{sp.travel.arrivalDate ?? '—'}</dd>
                <dt className="text-gray-500 dark:text-gray-400">Departure</dt><dd>{sp.travel.departureDate ?? '—'}</dd>
                {sp.travel.flyingFrom && (<><dt className="text-gray-500 dark:text-gray-400">Flying from</dt><dd>{sp.travel.flyingFrom}</dd></>)}
                {sp.travel.busFrom && (<><dt className="text-gray-500 dark:text-gray-400">Bus from</dt><dd>{sp.travel.busFrom}</dd></>)}
                {sp.travel.roomNote && (<><dt className="text-gray-500 dark:text-gray-400">Room</dt><dd>{sp.travel.roomNote}</dd></>)}
              </>
            )}
            {sp.plusOne && (
              <><dt className="text-gray-500 dark:text-gray-400">Plus-one</dt><dd>{sp.plusOne.name} · voucher {sp.plusOne.voucherCode} ({sp.plusOne.registered ? 'registered' : 'not registered yet'})</dd></>
            )}
          </dl>

          <div className="flex flex-wrap gap-2">
            {sp.canApprovePhoto && (
              <Button size="sm" loading={approvePhoto.isPending} onClick={() => approvePhoto.mutate(sp.id, { onSuccess: () => toast({ tone: 'success', title: 'Photo approved.' }), onError: (e) => toast({ tone: 'error', title: errorMessage(e) }) })}>
                Approve photo
              </Button>
            )}
            {sp.canSend && <Button size="sm" onClick={() => run('link')}>Send confirmation link</Button>}
            {sp.sent && <Button size="sm" variant="secondary" onClick={() => run('link/resend')}>Resend link</Button>}
            {sp.sent && <Button size="sm" variant="destructive" onClick={() => setRevokeOpen(true)}>Revoke link</Button>}
            {sp.plusOne && <Button size="sm" variant="destructive" onClick={() => setRemoveOpen(true)}>Remove plus-one</Button>}
          </div>

          <p className="text-xs text-gray-500 dark:text-gray-400">
            Only admins and the Programs lead can see travel details. The link needs a one-time code sent to the speaker's email, so a forwarded link alone won't open it.
          </p>

          <section className="flex flex-col gap-2">
            <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-50">Link access log</h3>
            <ul className="flex flex-col gap-1 text-sm">
              {sp.log.map((l, i) => (
                <li key={i} className="flex justify-between gap-3 text-gray-600 dark:text-gray-300">
                  <span>{l.what}</span><span className="text-gray-400 dark:text-gray-500">{new Date(l.at).toLocaleString('en-PH')}</span>
                </li>
              ))}
              {sp.log.length === 0 && <li className="text-gray-400 dark:text-gray-500">No activity yet.</li>}
            </ul>
          </section>

          <ReasonDialog open={revokeOpen} title="Revoke link" onClose={() => setRevokeOpen(false)} pending={action.isPending}
            onConfirm={(reason) => run('link/revoke', { reason }, () => setRevokeOpen(false))} />
          <ReasonDialog open={removeOpen} title="Remove plus-one" onClose={() => setRemoveOpen(false)} pending={action.isPending}
            onConfirm={(reason) => run('plus-one/remove', { reason }, () => setRemoveOpen(false))} />
        </div>
      )}
    </Drawer>
  )
}

function ReasonDialog({ open, title, onClose, onConfirm, pending }: { open: boolean; title: string; onClose: () => void; onConfirm: (reason: string) => void; pending: boolean }) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  return (
    <Dialog open={open} onClose={onClose} role="alertdialog" title={title}
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button variant="destructive" loading={pending} onClick={() => { if (!reason.trim()) { setError('Give a reason.'); return } onConfirm(reason.trim()) }}>{title}</Button></>}>
      <Field label="Reason" error={error ?? undefined}>
        {(c) => <Textarea {...c} rows={3} value={reason} onChange={(e) => { setReason(e.target.value); setError(null) }} />}
      </Field>
    </Dialog>
  )
}
