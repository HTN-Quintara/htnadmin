import { Badge } from '@/components/ui/Badge'
import type { Agenda, Session } from '../schemas'

export function Timeline({ days, canManage, onEdit }: { days: Agenda['days']; canManage: boolean; onEdit: (s: Session) => void }) {
  return (
    <div className="flex flex-col gap-6">
      {days.map((day) => (
        <section key={day.date} aria-label={day.heading} className="flex flex-col gap-3">
          <div className="flex flex-wrap items-baseline justify-between gap-2 border-b border-gray-200 pb-2 dark:border-white/10">
            <h2 className="text-base font-semibold text-gray-900 dark:text-gray-50">{day.heading}</h2>
            <span className="text-xs text-gray-500 dark:text-gray-400">
              {day.sessionCount} sessions{day.tentativeSpeakers > 0 ? ` · ${day.tentativeSpeakers} speakers still tentative` : ''}
            </span>
          </div>
          <ol className="flex flex-col gap-3">
            {day.slots.map((slot) => (
              <li key={slot.time} className="grid grid-cols-[72px_1fr] gap-3">
                <span className="pt-2 text-xs font-medium text-gray-500 tabular-nums dark:text-gray-400">{slot.time}</span>
                <div className="flex flex-wrap gap-2">
                  {slot.sessions.map((s) => <SessionCard key={s.id} session={s} canManage={canManage} onEdit={onEdit} />)}
                </div>
              </li>
            ))}
          </ol>
        </section>
      ))}
    </div>
  )
}

function SessionCard({ session, canManage, onEdit }: { session: Session; canManage: boolean; onEdit: (s: Session) => void }) {
  const roomText = session.rooms.map((r) => r.name).join(' + ') || 'No room'
  const who = session.speakers.map((sp) => sp.name).join(', ')
  const body = (
    <>
      <div className="flex items-center gap-2">
        <span className="text-xs text-gray-500 dark:text-gray-400">{session.kind} · {roomText}</span>
        {session.tentative && <Badge color="yellow">Tentative</Badge>}
      </div>
      <span className="text-sm font-semibold text-gray-900 dark:text-gray-50">{session.title}</span>
      {who && <span className="text-xs text-gray-600 dark:text-gray-300">{who}</span>}
    </>
  )
  const cls = 'flex min-w-56 flex-1 flex-col gap-1 rounded-md bg-white p-3 text-left shadow-sm ring-1 ring-gray-900/5 dark:bg-gray-900 dark:ring-white/10'
  return canManage
    ? <button type="button" className={cls + ' hover:ring-brand-300'} onClick={() => onEdit(session)}>{body}</button>
    : <div className={cls}>{body}</div>
}
