import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useAuth } from '@/app/auth/auth-context'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { PageSpinner } from '@/components/ui/Spinner'
import { ChevronLeftIcon } from '@/components/ui/icons'
import { readStation } from '@/features/checkin/station'
import { useEventQuery } from '@/features/events/api'
import type { EventDetail } from '@/features/events/schemas'
import { useMerchDashboard, useMerchDiscountsPreview, useSellMutation } from './api'
import { BuyerPanel } from './components/BuyerPanel'
import { CartPanel, type CartLine } from './components/CartPanel'
import { ProductGrid } from './components/ProductGrid'
import { PosReceipt } from './components/PosReceipt'
import type { BuyerHit, EventMerchSettings, MerchSale, MerchSaleMethod } from './schemas'

const pctField: Record<'Goon' | 'Apprentice' | 'Member' | 'Board', 'goonPct' | 'apprenticePct' | 'memberPct' | 'boardPct'> = {
  Goon: 'goonPct',
  Apprentice: 'apprenticePct',
  Member: 'memberPct',
  Board: 'boardPct',
}

function pctFor(settings: EventMerchSettings | null | undefined, level: BuyerHit['level']): number | null {
  if (!settings || level === 'Attendee') return null
  return settings[pctField[level]]
}

/** Merch POS kiosk (canvas frame 21): no sidebar, follows the check-in desk's kiosk precedent. */
export function PosPage() {
  const { eventId = '' } = useParams()
  const query = useEventQuery(eventId)

  if (query.isPending) return <PageSpinner label="Loading event" />
  if (query.isError) {
    return (
      <div className="mx-auto flex w-full max-w-lg flex-col gap-4 p-5">
        <Alert tone="red" live="alert" action={<Button variant="secondary" onClick={() => void query.refetch()}>Try again</Button>}>
          Could not load the event. {errorMessage(query.error)}
        </Alert>
      </div>
    )
  }

  return <Pos event={query.data} />
}

function Pos({ event }: { event: EventDetail }) {
  const { me } = useAuth()
  const [station] = useState(readStation)
  const dashboard = useMerchDashboard(event.id)
  const discounts = useMerchDiscountsPreview(event.id)
  const sell = useSellMutation(event.id)

  const [buyer, setBuyer] = useState<BuyerHit | null>(null)
  const [cart, setCart] = useState<CartLine[]>([])
  const [method, setMethod] = useState<MerchSaleMethod>('Cash')
  const [includesMembership, setIncludesMembership] = useState(false)
  const [completed, setCompleted] = useState<MerchSale | null>(null)
  // One key per distinct sale attempt: the same value is resent on every retry (a failed request, a
  // slow network, a double-tap) so the server can collapse duplicates into the original sale instead of
  // double-charging and double-decrementing stock. Only a reset (after success, or "New sale") issues a
  // fresh one.
  const [clientRequestId, setClientRequestId] = useState(() => crypto.randomUUID())

  const base = `/events/${encodeURIComponent(event.id)}/merch`

  function addToCart(itemId: string, itemName: string, variantId: string, variantLabel: string, unitPrice: number) {
    setCart((prev) => {
      const existing = prev.find((l) => l.variantId === variantId)
      if (existing) return prev.map((l) => (l.variantId === variantId ? { ...l, qty: l.qty + 1 } : l))
      return [...prev, { itemId, itemName, variantId, variantLabel, unitPrice, qty: 1 }]
    })
  }

  function setQty(variantId: string, qty: number) {
    setCart((prev) => (qty <= 0 ? prev.filter((l) => l.variantId !== variantId) : prev.map((l) => (l.variantId === variantId ? { ...l, qty } : l))))
  }

  function resetSale() {
    setBuyer(null)
    setCart([])
    setMethod('Cash')
    setIncludesMembership(false)
    setCompleted(null)
    setClientRequestId(crypto.randomUUID())
    sell.reset()
  }

  function pickBuyer(hit: BuyerHit) {
    setBuyer(hit)
    // A resolved Person (any level) already is a member of the organization — drop a pending membership
    // toggle rather than leave it silently stuck on a buyer who can never actually receive it.
    if (hit.kind === 'Person') setIncludesMembership(false)
  }

  // Phase 8B: the "Membership {year}" POS tile — available only when this event is the current main event
  // with a configured fee, mirroring the server's own `MembershipNotAvailable` check.
  const membershipFee = event.membershipFee ?? 0
  const membershipYear = event.membershipYear ?? null
  const membershipAvailable = event.isMainEvent && event.membershipFee != null && membershipYear != null

  const subtotal = cart.reduce((sum, l) => sum + l.unitPrice * l.qty, 0)
  const pct = buyer ? pctFor(discounts.data, buyer.level) : null
  const discountAmount = pct != null ? Math.round(subtotal * pct) / 100 : buyer && buyer.level !== 'Attendee' ? 0 : null
  const discountLabel = buyer && buyer.level !== 'Attendee' ? (pct != null ? `${buyer.level} discount (${pct}%)` : 'Discount') : null
  const membershipFeeAmount = includesMembership ? membershipFee : null
  const total = subtotal - (discountAmount ?? 0) + (membershipFeeAmount ?? 0)

  const membershipNote = !includesMembership
    ? null
    : buyer?.kind === 'Person'
      ? `${buyer.name} is already a ${buyer.level}. Remove membership from the cart.`
      : buyer
        ? `Membership will be added for ${buyer.name}.`
        : null

  function onSubmit() {
    if (!buyer || (cart.length === 0 && !includesMembership) || sell.isPending) return
    sell.mutate(
      {
        clientRequestId,
        buyerPersonId: buyer.personId,
        buyerAttendeeId: buyer.attendeeId,
        buyerName: buyer.name,
        lines: cart.map((l) => ({ variantId: l.variantId, qty: l.qty })),
        method,
        station,
        includesMembership,
      },
      { onSuccess: (sale) => setCompleted(sale) },
    )
  }

  return (
    <div className="flex min-h-dvh flex-col bg-gray-50 text-gray-900 dark:bg-gray-950 dark:text-gray-50">
      <header className="flex flex-wrap items-center gap-3 border-b border-gray-200 bg-white px-4 py-3 sm:px-6 dark:border-white/10 dark:bg-gray-900">
        <Link
          to={base}
          aria-label="Back to Merch"
          className="flex size-11 flex-none items-center justify-center rounded-lg text-gray-700 ring-1 ring-gray-300 ring-inset hover:bg-gray-50 dark:text-gray-200 dark:ring-white/15 dark:hover:bg-gray-800"
        >
          <ChevronLeftIcon size={20} />
        </Link>
        <div className="flex min-w-0 flex-col">
          <h1 className="text-base font-semibold">Merch POS · {event.name}</h1>
          <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
            Booth {station}
            {me ? ` · Cashier ${me.firstName} ${me.lastName}` : ''}
          </span>
        </div>
      </header>

      <main className="flex flex-1 flex-wrap items-start gap-5 p-4 sm:p-6">
        <section aria-label="Products" className="flex min-w-0 flex-[999_1_560px] flex-col gap-4">
          {dashboard.isPending ? <PageSpinner label="Loading items" /> : null}
          {dashboard.isError ? (
            <Alert tone="red" live="alert" action={<Button variant="secondary" onClick={() => void dashboard.refetch()}>Try again</Button>}>
              Could not load merch items. {errorMessage(dashboard.error)}
            </Alert>
          ) : null}
          {dashboard.data ? (
            dashboard.data.items.filter((i) => i.isActive).length === 0 ? (
              <Card className="p-8 text-center text-sm text-gray-600 dark:text-gray-400">No items in the catalog yet. Add one from the Merch page.</Card>
            ) : (
              <ProductGrid
                items={dashboard.data.items}
                onAdd={(item, variant) => addToCart(item.id, item.name, variant.id, variant.label, item.price)}
                membership={
                  membershipAvailable && membershipYear != null
                    ? {
                        year: membershipYear,
                        fee: membershipFee,
                        included: includesMembership,
                        onToggle: () => setIncludesMembership((v) => !v),
                      }
                    : null
                }
              />
            )
          ) : null}
        </section>

        <aside aria-label="Current sale" className="flex min-w-0 flex-[1_1_360px] flex-col gap-4">
          <Card className="p-4">
            <h2 className="mb-3 text-sm font-semibold text-gray-900 dark:text-gray-50">Current sale</h2>
            {completed ? (
              <PosReceipt sale={completed} onNewSale={resetSale} />
            ) : (
              <div className="flex flex-col gap-5">
                <BuyerPanel eventId={event.id} buyer={buyer} discountPct={pct} onPick={pickBuyer} onClear={() => setBuyer(null)} />
                <div aria-live="polite">
                  {sell.isError ? <Alert tone="red" live="alert">{errorMessage(sell.error)}</Alert> : null}
                </div>
                <CartPanel
                  lines={cart}
                  onQtyChange={setQty}
                  subtotal={subtotal}
                  discountLabel={discountLabel}
                  discountAmount={discountAmount}
                  total={total}
                  method={method}
                  onMethodChange={setMethod}
                  hasBuyer={!!buyer}
                  onSubmit={onSubmit}
                  submitting={sell.isPending}
                  membershipFeeAmount={membershipFeeAmount}
                  membershipYear={event.membershipYear}
                  membershipNote={membershipNote}
                />
              </div>
            )}
          </Card>
        </aside>
      </main>
    </div>
  )
}
