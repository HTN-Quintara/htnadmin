import { useState } from 'react'
import { useParams } from 'react-router'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { Skeleton } from '@/components/ui/Spinner'
import { TruckIcon } from '@/components/ui/icons'
import { useEventSuppliers } from './api'
import { HireFormDialog } from './components/HireFormDialog'
import { HiresTable } from './components/HiresTable'

/**
 * Minimal event-scoped hire management, implied by the backend's per-event hire endpoints even though the design
 * frames don't show it as its own screen (frame 18b's aside is read-only hire history; the actual hiring action —
 * create, money/status updates, void, delete-while-Quotation — needs a form somewhere). Gated Suppliers.View for
 * reads, Suppliers.Manage for the write actions below.
 */
export function EventSuppliersPage() {
  const { eventId = '' } = useParams()
  const canManage = useCan(Policies.SuppliersManage)
  const query = useEventSuppliers(eventId)
  const [adding, setAdding] = useState(false)

  if (query.isPending) return <div role="status" aria-label="Loading suppliers"><Skeleton className="h-64 w-full" /></div>
  if (query.isError) {
    return (
      <Alert tone="red" action={<Button size="sm" variant="secondary" onClick={() => query.refetch()}>Try again</Button>}>
        Could not load suppliers for this event. {errorMessage(query.error)}
      </Alert>
    )
  }

  const hires = query.data

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-gray-600 dark:text-gray-300">Suppliers hired for this event, their budget category, owning committee and status.</p>
        {canManage && hires.length > 0 ? <Button size="sm" onClick={() => setAdding(true)}>+ Hire supplier</Button> : null}
      </div>

      {hires.length === 0 ? (
        <EmptyState
          icon={<TruckIcon />}
          title="No suppliers hired yet"
          description="Hire a venue, caterer, printer, tech/AV, merch or transport supplier for this event."
          action={canManage ? <Button onClick={() => setAdding(true)}>+ Hire supplier</Button> : undefined}
        />
      ) : (
        <HiresTable eventId={eventId} hires={hires} canManage={canManage} />
      )}

      {canManage && <HireFormDialog open={adding} onClose={() => setAdding(false)} eventId={eventId} />}
    </div>
  )
}
