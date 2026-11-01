import { formatDate } from '@/lib/format'
import type { DirectoryContactHistory, DirectoryContactHistoryKind } from '../schemas'

const kindLabels: Record<DirectoryContactHistoryKind, string> = {
  HolderChanged: 'Head changed',
  Verified: 'Verified',
  StillCorrect: 'Still correct',
  Bounced: 'Email bounced',
}

/** Append-only log of verification and holder-change events for an organization's contacts. */
export function HistoryLog({ history }: { history: DirectoryContactHistory[] }) {
  if (history.length === 0) {
    return <p className="text-sm text-gray-500 dark:text-gray-400">No history yet.</p>
  }
  return (
    <ol className="flex flex-col gap-3">
      {history.map((h) => (
        <li key={h.id} className="flex flex-col gap-0.5 border-l-2 border-gray-200 pl-3 text-sm dark:border-white/10">
          <div className="flex items-center justify-between gap-2">
            <span className="font-medium text-gray-900 dark:text-gray-50">{kindLabels[h.kind]}</span>
            <span className="text-xs text-gray-500 dark:text-gray-400">{formatDate(h.at)}</span>
          </div>
          {h.kind === 'HolderChanged' && h.priorPosition && h.priorHolderName ? (
            <p className="text-xs text-gray-600 dark:text-gray-400">was: {h.priorPosition} — {h.priorHolderName}</p>
          ) : null}
          {h.note ? <p className="text-xs text-gray-600 dark:text-gray-400">{h.note}</p> : null}
        </li>
      ))}
    </ol>
  )
}
