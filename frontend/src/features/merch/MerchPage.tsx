import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { Stat } from '@/components/ui/Stat'
import { Skeleton } from '@/components/ui/Spinner'
import { buttonClass } from '@/components/ui/button-styles'
import { formatPeso } from '@/lib/format'
import { useMerchDashboard, useMerchDiscounts } from './api'
import { DiscountsPanel } from './components/DiscountsPanel'
import { InventoryTable } from './components/InventoryTable'
import { ItemFormDialog } from './components/ItemFormDialog'
import { MoveToStoreDialog } from './components/MoveToStoreDialog'
import { SalesLogTable } from './components/SalesLogTable'

/** Merch event tab (canvas frame 20): KPIs, inventory, discounts aside, sales log. */
export function MerchPage() {
  const { eventId = '' } = useParams()
  const canManage = useCan(Policies.MerchManage)
  const dashboard = useMerchDashboard(eventId)
  const [adding, setAdding] = useState(false)
  const [moving, setMoving] = useState(false)

  if (dashboard.isPending) {
    return (
      <div role="status" aria-label="Loading merch">
        <Skeleton className="h-64 w-full" />
      </div>
    )
  }

  if (dashboard.isError) {
    return (
      <Alert
        tone="red"
        live="alert"
        action={
          <Button size="sm" variant="secondary" onClick={() => void dashboard.refetch()}>
            Try again
          </Button>
        }
      >
        Could not load merch for this event. {errorMessage(dashboard.error)}
      </Alert>
    )
  }

  const { kpi, items, sales } = dashboard.data
  const lowStockVariants = items.flatMap((i) => i.variants.filter((v) => v.lowStock && v.isActive).map((v) => `${i.name} (${v.label})`))

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-gray-600 dark:text-gray-300">Inventory, discounts and sales for this event's merch booth.</p>
        <div className="flex items-center gap-2">
          {canManage ? (
            <Button variant="secondary" onClick={() => setMoving(true)}>
              Move leftovers to store
            </Button>
          ) : null}
          {canManage ? <Button onClick={() => setAdding(true)}>+ Item</Button> : null}
          <Link to={`/events/${encodeURIComponent(eventId)}/pos`} className={buttonClass('primary')}>
            Open POS
          </Link>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Stat label="Merch sales" value={kpi.totalSales != null ? formatPeso(kpi.totalSales) : '—'} sub={`${kpi.orderCount} orders`} />
        <Stat label="Items sold" value={kpi.itemsSold} sub={`${kpi.itemsSold} of ${kpi.itemsInStock} in stock`} />
        <Stat
          label="Discounts given"
          value={kpi.discountsGiven != null ? formatPeso(kpi.discountsGiven) : '—'}
          sub="Goons, apprentices, members"
        />
        <Stat
          label="Low stock"
          value={`${kpi.lowStockVariantCount} variants`}
          sub={lowStockVariants.length > 0 ? lowStockVariants.slice(0, 2).join(', ') : 'None yet'}
        />
      </div>

      <div className="grid grid-cols-1 gap-5 lg:grid-cols-[2fr_1fr]">
        <InventoryTable items={items} sales={sales} />
        <DiscountsSlot eventId={eventId} canManage={canManage} />
      </div>

      <SalesLogTable eventId={eventId} sales={sales} canManage={canManage} />

      {canManage ? <ItemFormDialog open={adding} onClose={() => setAdding(false)} eventId={eventId} /> : null}
      {canManage && moving ? <MoveToStoreDialog eventId={eventId} items={items} onClose={() => setMoving(false)} /> : null}
    </div>
  )
}

/**
 * Its own query so a committee lead (who can sell but not manage) never flashes a loading state tied to an
 * endpoint they can't call, and the board's discount panel loads independently of the rest of the dashboard.
 */
function DiscountsSlot({ eventId, canManage }: { eventId: string; canManage: boolean }) {
  const discounts = useMerchDiscounts(eventId, canManage)

  if (!canManage) {
    return (
      <Card>
        <CardHeader title="Discounts for this event" description="Visible to the board only." />
      </Card>
    )
  }

  if (discounts.isPending) {
    return (
      <Card>
        <CardHeader title="Discounts for this event" />
        <div className="p-5">
          <Skeleton className="h-40 w-full" />
        </div>
      </Card>
    )
  }

  if (discounts.isError) {
    return (
      <Card>
        <CardHeader title="Discounts for this event" />
        <div className="p-5">
          <Alert tone="red" live="alert" action={<Button size="sm" variant="secondary" onClick={() => void discounts.refetch()}>Try again</Button>}>
            Could not load discounts. {errorMessage(discounts.error)}
          </Alert>
        </div>
      </Card>
    )
  }

  return <DiscountsPanel eventId={eventId} settings={discounts.data} canManage={canManage} />
}
