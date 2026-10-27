import { useState } from 'react'
import { useParams } from 'react-router'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { Skeleton } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import { useLeadsBoardQuery } from './api'
import type { LeadKind } from './schemas'
import { LeadColumn } from './components/LeadColumn'
import { LeadDialog } from './components/LeadDialog'

/** Frame 17: sponsor and speaker leads, board-only. */
export function LeadsPage() {
  const { eventId = '' } = useParams()
  const [kind, setKind] = useState<LeadKind>('Sponsor')
  const [adding, setAdding] = useState(false)
  const query = useLeadsBoardQuery(eventId, kind)
  useToast()

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <SegmentedControl
          aria-label="Lead type"
          value={kind}
          onChange={setKind}
          options={[
            { value: 'Sponsor', label: 'Sponsor leads' },
            { value: 'Speaker', label: 'Speaker leads' },
          ]}
        />
        <Button size="sm" onClick={() => setAdding(true)}>+ {kind === 'Sponsor' ? 'Sponsor' : 'Speaker'} lead</Button>
      </div>

      {query.isPending ? (
        <div role="status" aria-label="Loading leads"><Skeleton className="h-64 w-full" /></div>
      ) : query.isError ? (
        <Alert tone="red" action={<Button size="sm" variant="secondary" onClick={() => query.refetch()}>Try again</Button>}>
          Could not load leads. {errorMessage(query.error)}
        </Alert>
      ) : (
        <>
          <div className="flex flex-wrap gap-6 text-sm text-gray-600 dark:text-gray-300">
            {query.data.stats.map((s) => (
              <span key={s.label}>{s.label} <strong className="font-semibold text-gray-900 dark:text-gray-50">{s.value}</strong></span>
            ))}
          </div>
          <div className="overflow-x-auto pb-2">
            <div className="flex min-w-[1120px] items-start gap-3">
              {query.data.columns.map((col) => (
                <LeadColumn key={col.status} eventId={eventId} column={col} />
              ))}
            </div>
          </div>
        </>
      )}

      <LeadDialog key={adding ? 'open' : 'closed'} eventId={eventId} kind={kind} open={adding} onClose={() => setAdding(false)} />
    </div>
  )
}
