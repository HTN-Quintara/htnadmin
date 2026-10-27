import { useState } from 'react'
import { useParams } from 'react-router'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { Skeleton } from '@/components/ui/Spinner'
import { CalendarIcon } from '@/components/ui/icons'
import { useAgendaQuery } from './api'
import type { Session } from './schemas'
import { Timeline } from './components/Timeline'
import { SessionDialog } from './components/SessionDialog'
import { RoomsDialog } from './components/RoomsDialog'

/** Frame 16: the programme. Reads for anyone on the event; writes for board or the Programs lead (canManage). */
export function AgendaPage() {
  const { eventId = '' } = useParams()
  const query = useAgendaQuery(eventId)
  const [editing, setEditing] = useState<Session | null>(null)
  const [adding, setAdding] = useState(false)
  const [rooms, setRooms] = useState(false)

  if (query.isPending) return <div role="status" aria-label="Loading agenda"><Skeleton className="h-64 w-full" /></div>
  if (query.isError) {
    return (
      <Alert tone="red" action={<Button size="sm" variant="secondary" onClick={() => query.refetch()}>Try again</Button>}>
        Could not load the agenda. {errorMessage(query.error)}
      </Alert>
    )
  }

  const agenda = query.data
  const canManage = agenda.canManage

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          {agenda.rooms.filter((r) => r.isActive).map((r) => (
            <span key={r.id} className="inline-flex items-center gap-1.5 text-xs text-gray-600 dark:text-gray-300">
              <span aria-hidden className="h-2.5 w-2.5 rounded-full" style={{ background: r.colorHex }} />{r.name}
            </span>
          ))}
        </div>
        {canManage && (
          <div className="flex gap-2">
            <Button size="sm" variant="secondary" onClick={() => setRooms(true)}>Rooms</Button>
            <Button size="sm" onClick={() => setAdding(true)}>+ Session</Button>
          </div>
        )}
      </div>

      {agenda.days.length === 0 ? (
        <EmptyState
          icon={<CalendarIcon />}
          title="No sessions yet"
          description={canManage ? 'Add rooms, then build the programme session by session.' : 'The programme has not been published yet.'}
          action={canManage ? <Button onClick={() => setAdding(true)}>+ Session</Button> : undefined}
        />
      ) : (
        <Timeline days={agenda.days} canManage={canManage} onEdit={setEditing} />
      )}

      {canManage && (
        <>
          <SessionDialog key={adding ? 'add' : 'add-closed'} eventId={eventId} rooms={agenda.rooms} open={adding} onClose={() => setAdding(false)} />
          <SessionDialog key={editing?.id ?? 'edit-closed'} eventId={eventId} rooms={agenda.rooms} session={editing ?? undefined} open={editing !== null} onClose={() => setEditing(null)} />
          <RoomsDialog key={rooms ? 'rooms' : 'rooms-closed'} eventId={eventId} rooms={agenda.rooms} open={rooms} onClose={() => setRooms(false)} />
        </>
      )}
    </div>
  )
}
