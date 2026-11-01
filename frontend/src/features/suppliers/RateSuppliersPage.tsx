import { useState } from 'react'
import { useParams } from 'react-router'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardBody } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { ProgressBar } from '@/components/ui/ProgressBar'
import { Skeleton } from '@/components/ui/Spinner'
import { Textarea } from '@/components/ui/Input'
import { CheckSquareIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { cn } from '@/lib/cn'
import { useSubmitRatingMutation, useSuppliersToRate } from './api'
import { hireAgainValues, type EventSupplier, type HireAgain, type SupplierRatingTag } from './schemas'
import { StarRating } from './components/StarRating'
import { TagChips } from './components/TagChips'
import { hireAgainLabel } from './components/labels'

/** Frame 18c: rate your suppliers, post-event, per committee lead (or board). */
export function RateSuppliersPage() {
  const { eventId = '' } = useParams()
  const query = useSuppliersToRate(eventId)
  // The "N of M" count is session-local, not refetched from the server (which only ever returns what's left):
  // every hire that has been successfully rated in this visit is added here by the card's own onSuccess, an event
  // handler, so this never needs an effect that watches query.data.
  const [ratedIds, setRatedIds] = useState<ReadonlySet<string>>(new Set())

  if (query.isPending) return <div role="status" aria-label="Loading suppliers to rate"><Skeleton className="h-64 w-full" /></div>
  if (query.isError) {
    return (
      <Alert tone="red" action={<Button size="sm" variant="secondary" onClick={() => query.refetch()}>Try again</Button>}>
        Could not load suppliers to rate. {errorMessage(query.error)}
      </Alert>
    )
  }

  const remaining = query.data
  const total = remaining.length + ratedIds.size
  const done = ratedIds.size

  return (
    <div className="flex flex-col gap-5">
      <PageHeader title="Rate your suppliers" description="Rate the suppliers your committee used for this event." />

      {total > 0 ? (
        <div className="flex flex-col gap-1.5">
          <ProgressBar value={total === 0 ? 100 : (done / total) * 100} label={`${done} of ${total} rated`} />
          <p className="text-sm text-gray-600 dark:text-gray-400">{done} of {total} rated</p>
        </div>
      ) : null}

      {remaining.length === 0 ? (
        <EmptyState
          icon={<CheckSquareIcon />}
          title={total === 0 ? 'Nothing to rate yet' : 'All done'}
          description={
            total === 0
              ? "Either this event hasn't ended yet, or your committee has no supplier hires."
              : "You've rated every supplier your committee used for this event."
          }
        />
      ) : (
        <div className="flex flex-col gap-4">
          {remaining.map((hire) => (
            <SupplierRatingCard
              key={hire.id}
              eventId={eventId}
              hire={hire}
              onRated={(id) => setRatedIds((prev) => new Set(prev).add(id))}
            />
          ))}
        </div>
      )}

      <p className="text-xs text-gray-500 dark:text-gray-400">Reviews show your name and the event; prices not shown.</p>
    </div>
  )
}

function SupplierRatingCard({
  eventId,
  hire,
  onRated,
}: {
  eventId: string
  hire: EventSupplier
  onRated: (hireId: string) => void
}) {
  const submit = useSubmitRatingMutation(eventId)
  const { toast } = useToast()
  const [stars, setStars] = useState(0)
  const [tags, setTags] = useState<SupplierRatingTag[]>([])
  const [hireAgain, setHireAgain] = useState<HireAgain | null>(null)
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const hireAgainGroupName = `hire-again-${hire.id}`

  const canSubmit = stars > 0 && hireAgain !== null

  function onSubmit() {
    if (!canSubmit || submit.isPending) return
    submit.mutate(
      { hireId: hire.id, body: { stars, tags, hireAgain, note: note.trim() || null } },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: `Rated ${hire.supplierName}.` })
          onRated(hire.id)
        },
        onError: (e) => setError(errorMessage(e)),
      },
    )
  }

  return (
    <Card>
      <CardBody className="flex flex-col gap-4">
        <div>
          <h2 className="text-base font-semibold text-gray-900 dark:text-gray-50">{hire.supplierName}</h2>
          <p className="text-sm text-gray-600 dark:text-gray-400">{hire.categoryName}</p>
        </div>

        <div className="flex flex-col gap-1.5">
          <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Stars</span>
          <StarRating name={`stars-${hire.id}`} value={stars} onChange={setStars} aria-label={`Rate ${hire.supplierName}`} disabled={submit.isPending} />
        </div>

        <div className="flex flex-col gap-1.5">
          <span className="text-sm font-medium text-gray-700 dark:text-gray-200">What was it like?</span>
          <TagChips value={tags} onChange={setTags} aria-label={`Tags for ${hire.supplierName}`} disabled={submit.isPending} />
        </div>

        <div className="flex flex-col gap-1.5">
          <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Hire again?</span>
          <div role="radiogroup" aria-label={`Hire ${hire.supplierName} again?`} className="flex gap-2">
            {hireAgainValues.map((h) => {
              const checked = hireAgain === h
              return (
                <label
                  key={h}
                  className={cn(
                    'flex min-h-11 min-w-20 cursor-pointer items-center justify-center rounded-md px-3 text-sm font-medium ring-1 ring-inset',
                    submit.isPending && 'cursor-not-allowed opacity-50',
                    checked
                      ? 'bg-brand-50 text-brand-700 ring-brand-600 dark:bg-brand-300/10 dark:text-brand-300 dark:ring-brand-300'
                      : 'bg-white text-gray-700 ring-gray-300 hover:bg-gray-50 dark:bg-gray-900 dark:text-gray-200 dark:ring-white/15 dark:hover:bg-gray-800',
                  )}
                >
                  <input
                    type="radio"
                    name={hireAgainGroupName}
                    value={h}
                    checked={checked}
                    disabled={submit.isPending}
                    onChange={() => setHireAgain(h)}
                    className="sr-only"
                  />
                  {hireAgainLabel(h)}
                </label>
              )
            })}
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor={`note-${hire.id}`} className="text-sm font-medium text-gray-700 dark:text-gray-200">
            Note <span className="font-normal text-gray-600 dark:text-gray-400">(optional)</span>
          </label>
          <Textarea id={`note-${hire.id}`} rows={2} value={note} onChange={(e) => setNote(e.target.value)} disabled={submit.isPending} />
        </div>

        {error ? <p role="alert" className="text-xs font-medium text-red-700 dark:text-red-400">{error}</p> : null}

        <div>
          <Button onClick={onSubmit} disabled={!canSubmit} loading={submit.isPending}>
            Submit rating
          </Button>
        </div>
      </CardBody>
    </Card>
  )
}
