import { Badge } from '@/components/ui/Badge'
import { EmptyState } from '@/components/ui/EmptyState'
import { formatDate, formatPeso } from '@/lib/format'
import { cn } from '@/lib/cn'
import type { PopUp } from '../schemas'
import { popUpStatusBadgeColor, popUpStatusLabel } from './labels'

function whenWhere(p: PopUp): string {
  const venue = p.venueOrgName ?? p.venueText ?? 'No venue set'
  return `${formatDate(p.startsAt)} · ${venue}`
}

function statusText(p: PopUp): string {
  if (p.status !== 'ClosedMissing') return popUpStatusLabel(p.status)
  const missing = p.allocations.reduce((n, a) => n + (a.qtyMissing ?? 0), 0)
  return `Closed · ${missing} missing`
}

/** Left column of the Pop-up stores tab (canvas PopUps.dc.html): one card per pop-up. */
export function PopUpList({ popUps, selectedId, onSelect }: { popUps: PopUp[]; selectedId: string | null; onSelect: (id: string) => void }) {
  if (popUps.length === 0) {
    return <EmptyState title="No pop-up stores yet" description='Create one with "+ Pop-up store" once a committee is allowed to run them.' />
  }

  return (
    <ul className="flex flex-col gap-2">
      {popUps.map((p) => {
        const soldCount = p.allocations.reduce((n, a) => n + a.qtySold, 0)
        return (
          <li key={p.id}>
            <button
              type="button"
              aria-pressed={selectedId === p.id}
              onClick={() => onSelect(p.id)}
              className={cn(
                'flex w-full flex-col gap-1.5 rounded-lg p-4 text-left ring-1 ring-inset transition-colors',
                selectedId === p.id
                  ? 'bg-brand-50 ring-2 ring-brand-600 dark:bg-brand-300/10 dark:ring-brand-300'
                  : 'bg-white ring-gray-200 hover:bg-gray-50 dark:bg-gray-900 dark:ring-white/10 dark:hover:bg-gray-800',
              )}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="truncate font-semibold text-gray-900 dark:text-gray-50">{p.name}</span>
                <Badge color={popUpStatusBadgeColor(p.status)}>{statusText(p)}</Badge>
              </div>
              <span className="text-xs text-gray-600 dark:text-gray-400">{whenWhere(p)}</span>
              <span className="text-xs text-gray-600 dark:text-gray-400">
                {soldCount} sold · {p.totalSales != null ? formatPeso(p.totalSales) : '—'} · {p.team.length} on team
              </span>
            </button>
          </li>
        )
      })}
    </ul>
  )
}
