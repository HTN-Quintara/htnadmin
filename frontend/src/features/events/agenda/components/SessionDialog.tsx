import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input, Select, Checkbox } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useDeleteSessionMutation, useSaveSessionMutation, useSpeakerOptionsQuery } from '../api'
import { sessionFormSchema, sessionKinds, speakerRoles, type Room, type Session, type SessionFormValues, type SpeakerRole } from '../schemas'

type Speaker = { speakerId: string; role: SpeakerRole }

export function SessionDialog({ eventId, rooms, session, open, onClose }: { eventId: string; rooms: Room[]; session?: Session; open: boolean; onClose: () => void }) {
  const save = useSaveSessionMutation(eventId)
  const del = useDeleteSessionMutation(eventId)
  const options = useSpeakerOptionsQuery(eventId, open)
  const { toast } = useToast()
  const manila = session ? toManila(session.startsAt) : null
  const [roomIds, setRoomIds] = useState<string[]>(session?.rooms.map((r) => r.id) ?? [])
  const [speakers, setSpeakers] = useState<Speaker[]>(session?.speakers.map((s) => ({ speakerId: s.speakerId, role: s.role as SpeakerRole })) ?? [])
  const [roomError, setRoomError] = useState<string | null>(null)

  const { register, handleSubmit, setError, formState } = useForm<SessionFormValues>({
    resolver: zodResolver(sessionFormSchema),
    defaultValues: {
      title: session?.title ?? '',
      kind: session?.kind ?? 'Talk',
      date: manila?.date ?? '',
      time: manila?.time ?? '',
      durationMinutes: session ? String(session.durationMinutes) : '30',
      isPublic: session?.isPublic ?? true,
      notes: session?.notes ?? '',
    },
  })

  function onSubmit(values: SessionFormValues) {
    if (save.isPending) return
    if (values.kind !== 'Break' && roomIds.length === 0) { setRoomError('Pick at least one room.'); return }
    const body = {
      title: values.title,
      kind: values.kind,
      startsAt: `${values.date}T${values.time}:00+08:00`,
      durationMinutes: Number(values.durationMinutes) || 0,
      isPublic: values.isPublic,
      notes: values.notes || null,
      roomIds,
      speakers: speakers.map((s, i) => ({ speakerId: s.speakerId, role: s.role, sortOrder: i })),
    }
    save.mutate({ id: session?.id, body }, {
      onSuccess: () => { toast({ tone: 'success', title: session ? 'Session saved.' : 'Session added.' }); onClose() },
      onError: (e) => {
        const fields = isApiError(e) ? e.fieldErrors : {}
        for (const [f, m] of Object.entries(fields)) setError(f as keyof SessionFormValues, { message: m })
        if (!Object.keys(fields).length) setError('title', { message: errorMessage(e) })
      },
    })
  }

  const chosen = new Set(speakers.map((s) => s.speakerId))

  return (
    <Dialog open={open} onClose={onClose} title={session ? 'Edit session' : 'Add session'}
      footer={
        <div className="flex w-full items-center justify-between">
          {session
            ? <Button variant="destructive" loading={del.isPending} onClick={() => del.mutate(session.id, { onSuccess: () => { toast({ tone: 'success', title: 'Session deleted.' }); onClose() }, onError: (e) => toast({ tone: 'error', title: errorMessage(e) }) })}>Delete</Button>
            : <span />}
          <div className="flex gap-2">
            <Button variant="secondary" onClick={onClose}>Cancel</Button>
            <Button form="session-form" type="submit" loading={save.isPending}>Save</Button>
          </div>
        </div>
      }>
      <form id="session-form" className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
        <Field label="Title" error={formState.errors.title?.message}>{(c) => <Input {...c} {...register('title')} />}</Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Type">{(c) => <Select {...c} {...register('kind')}>{sessionKinds.map((k) => <option key={k} value={k}>{k}</option>)}</Select>}</Field>
          <Field label="Duration (min)" error={formState.errors.durationMinutes?.message}>{(c) => <Input inputMode="numeric" {...c} {...register('durationMinutes')} />}</Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Date" error={formState.errors.date?.message}>{(c) => <Input type="date" {...c} {...register('date')} />}</Field>
          <Field label="Start" error={formState.errors.time?.message}>{(c) => <Input type="time" {...c} {...register('time')} />}</Field>
        </div>
        <fieldset className="flex flex-col gap-1">
          <legend className="text-sm font-medium text-gray-700 dark:text-gray-200">Rooms</legend>
          <div className="flex flex-wrap gap-3">
            {rooms.filter((r) => r.isActive).map((r) => (
              <label key={r.id} className="flex items-center gap-2 text-sm">
                <Checkbox checked={roomIds.includes(r.id)} onChange={(e) => { setRoomError(null); setRoomIds((ids) => e.target.checked ? [...ids, r.id] : ids.filter((x) => x !== r.id)) }} />
                {r.name}
              </label>
            ))}
          </div>
          {roomError && <span className="text-xs text-red-700 dark:text-red-400">{roomError}</span>}
        </fieldset>
        <fieldset className="flex flex-col gap-2">
          <legend className="text-sm font-medium text-gray-700 dark:text-gray-200">Speakers</legend>
          {speakers.map((sp, i) => (
            <div key={sp.speakerId} className="flex items-center gap-2">
              <span className="flex-1 text-sm">{options.data?.find((o) => o.id === sp.speakerId)?.name ?? 'Speaker'}</span>
              <Select controlSize="md" value={sp.role} onChange={(e) => setSpeakers((list) => list.map((x, j) => j === i ? { ...x, role: e.target.value as SpeakerRole } : x))} aria-label="Role">
                {speakerRoles.map((r) => <option key={r} value={r}>{r}</option>)}
              </Select>
              <Button size="sm" variant="ghost" onClick={() => setSpeakers((list) => list.filter((_, j) => j !== i))}>Remove</Button>
            </div>
          ))}
          <Select
            controlSize="md"
            value=""
            aria-label="Add speaker from leads"
            onChange={(e) => { if (e.target.value) { setSpeakers((list) => [...list, { speakerId: e.target.value, role: 'Speaker' }]); e.target.value = '' } }}
          >
            <option value="">+ Add speaker from leads</option>
            {(options.data ?? []).filter((o) => !chosen.has(o.id)).map((o) => <option key={o.id} value={o.id}>{o.name}{o.status !== 'Confirmed' ? ' (tentative)' : ''}</option>)}
          </Select>
        </fieldset>
        <label className="flex items-center gap-2 text-sm"><Checkbox {...register('isPublic')} /> Show on the public registration page</label>
      </form>
    </Dialog>
  )
}

function toManila(iso: string): { date: string; time: string } {
  // The API sends an offset ISO; format its Manila wall-clock for the date/time inputs.
  const d = new Date(iso)
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Manila', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false }).formatToParts(d)
  const get = (t: string) => parts.find((p) => p.type === t)?.value ?? ''
  return { date: `${get('year')}-${get('month')}-${get('day')}`, time: `${get('hour')}:${get('minute')}` }
}
