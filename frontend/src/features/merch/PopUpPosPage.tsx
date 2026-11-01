import { useMemo, useState } from 'react'
import { Link, useParams } from 'react-router'
import { useAuth } from '@/app/auth/auth-context'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { PageSpinner } from '@/components/ui/Spinner'
import { ChevronLeftIcon } from '@/components/ui/icons'
import { readStation } from '@/features/checkin/station'
import { useLeftoverInventory, usePopUpQuery, useSellPopUpMutation } from './api'
import { CartPanel, type CartLine } from './components/CartPanel'
import { PopUpBuyerPanel } from './components/PopUpBuyerPanel'
import { PopUpProductGrid, type PopUpProduct } from './components/PopUpProductGrid'
import { PosReceipt } from './components/PosReceipt'
import type { BuyerHit, MerchSale, MerchSaleMethod, PopUp } from './schemas'

const pctField: Record<'Goon' | 'Apprentice' | 'Member' | 'Board', 'goonPct' | 'apprenticePct' | 'memberPct' | 'boardPct'> = {
  Goon: 'goonPct',
  Apprentice: 'apprenticePct',
  Member: 'memberPct',
  Board: 'boardPct',
}

/**
 * Pop-up POS kiosk (Phase 8C), reusing 8A's cart/totals/payment UI scoped to this pop-up's own allocated stock.
 * Differences from the event POS: no "Scan QR" (a pop-up isn't tied to one event's tickets), buyer search is
 * People only (plus a free-text walk-in), no membership tile, and the sale posts to `/pop-ups/{id}/pos/sale`.
 */
export function PopUpPosPage() {
  const { id = '' } = useParams()
  const popUp = usePopUpQuery(id)
  const leftover = useLeftoverInventory()

  if (popUp.isPending || leftover.isPending) return <PageSpinner label="Loading pop-up" />
  if (popUp.isError || !popUp.data) {
    return (
      <div className="mx-auto flex w-full max-w-lg flex-col gap-4 p-5">
        <Alert tone="red" live="alert" action={<Button variant="secondary" onClick={() => void popUp.refetch()}>Try again</Button>}>
          Could not load this pop-up. {errorMessage(popUp.error)}
        </Alert>
      </div>
    )
  }
  if (leftover.isError) {
    return (
      <div className="mx-auto flex w-full max-w-lg flex-col gap-4 p-5">
        <Alert tone="red" live="alert" action={<Button variant="secondary" onClick={() => void leftover.refetch()}>Try again</Button>}>
          Could not load stock prices. {errorMessage(leftover.error)}
        </Alert>
      </div>
    )
  }

  return <Pos popUp={popUp.data} priceByVariant={new Map((leftover.data ?? []).map((v) => [v.variantId, v.price]))} />
}

function Pos({ popUp, priceByVariant }: { popUp: PopUp; priceByVariant: Map<string, number> }) {
  const { me } = useAuth()
  const [station] = useState(readStation)
  const sell = useSellPopUpMutation(popUp.id)

  const [buyer, setBuyer] = useState<BuyerHit | null>(null)
  const [cart, setCart] = useState<CartLine[]>([])
  const [method, setMethod] = useState<MerchSaleMethod>('Cash')
  const [completed, setCompleted] = useState<MerchSale | null>(null)
  const [clientRequestId, setClientRequestId] = useState(() => crypto.randomUUID())

  const products: PopUpProduct[] = useMemo(
    () => popUp.allocations.map((a) => ({ ...a, price: priceByVariant.get(a.variantId) ?? 0 })),
    [popUp.allocations, priceByVariant],
  )

  function addToCart(p: PopUpProduct) {
    setCart((prev) => {
      const existing = prev.find((l) => l.variantId === p.variantId)
      if (existing) return prev.map((l) => (l.variantId === p.variantId ? { ...l, qty: l.qty + 1 } : l))
      return [...prev, { itemId: p.variantId, itemName: p.itemName, variantId: p.variantId, variantLabel: p.variantLabel, unitPrice: p.price, qty: 1 }]
    })
  }

  function setQty(variantId: string, qty: number) {
    setCart((prev) => (qty <= 0 ? prev.filter((l) => l.variantId !== variantId) : prev.map((l) => (l.variantId === variantId ? { ...l, qty } : l))))
  }

  function resetSale() {
    setBuyer(null)
    setCart([])
    setMethod('Cash')
    setCompleted(null)
    setClientRequestId(crypto.randomUUID())
    sell.reset()
  }

  const subtotal = cart.reduce((sum, l) => sum + l.unitPrice * l.qty, 0)
  // Only "Custom" discount mode is known client-side (the DTO's pct columns are null otherwise, since
  // "Same as default" reads the live org defaults, not something this pop-up's own row carries) — see the
  // report for why. For the other modes the preview shows no discount; the server still applies the right one.
  const buyerLevel = buyer && buyer.level !== 'Attendee' ? buyer.level : null
  const pct = buyerLevel && popUp.discountMode === 'Custom' ? popUp[pctField[buyerLevel]] : null
  const discountAmount = pct != null ? Math.round(subtotal * pct) / 100 : null
  const discountLabel = pct != null ? `${buyerLevel} discount (${pct}%)` : null
  const total = subtotal - (discountAmount ?? 0)

  function onSubmit() {
    if (!buyer || cart.length === 0 || sell.isPending) return
    sell.mutate(
      {
        clientRequestId,
        buyerPersonId: buyer.personId,
        buyerAttendeeId: null,
        buyerName: buyer.name,
        lines: cart.map((l) => ({ variantId: l.variantId, qty: l.qty })),
        method,
        station,
      },
      { onSuccess: (sale) => setCompleted(sale) },
    )
  }

  return (
    <div className="flex min-h-dvh flex-col bg-gray-50 text-gray-900 dark:bg-gray-950 dark:text-gray-50">
      <header className="flex flex-wrap items-center gap-3 border-b border-gray-200 bg-white px-4 py-3 sm:px-6 dark:border-white/10 dark:bg-gray-900">
        <Link
          to="/store"
          aria-label="Back to the Merch store"
          className="flex size-11 flex-none items-center justify-center rounded-lg text-gray-700 ring-1 ring-gray-300 ring-inset hover:bg-gray-50 dark:text-gray-200 dark:ring-white/15 dark:hover:bg-gray-800"
        >
          <ChevronLeftIcon size={20} />
        </Link>
        <div className="flex min-w-0 flex-col">
          <h1 className="text-base font-semibold">Pop-up POS · {popUp.name}</h1>
          <span className="text-[0.8125rem] text-gray-600 dark:text-gray-400">
            Booth {station}
            {me ? ` · Cashier ${me.firstName} ${me.lastName}` : ''}
          </span>
        </div>
      </header>

      <main className="flex flex-1 flex-wrap items-start gap-5 p-4 sm:p-6">
        <section aria-label="Products" className="flex min-w-0 flex-[999_1_560px] flex-col gap-4">
          <PopUpProductGrid products={products} onAdd={addToCart} />
        </section>

        <aside aria-label="Current sale" className="flex min-w-0 flex-[1_1_360px] flex-col gap-4">
          <Card className="p-4">
            <h2 className="mb-3 text-sm font-semibold text-gray-900 dark:text-gray-50">Current sale</h2>
            {completed ? (
              <PosReceipt sale={completed} onNewSale={resetSale} />
            ) : (
              <div className="flex flex-col gap-5">
                <PopUpBuyerPanel popUpId={popUp.id} buyer={buyer} discountPct={pct} onPick={setBuyer} onClear={() => setBuyer(null)} />
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
                />
              </div>
            )}
          </Card>
        </aside>
      </main>
    </div>
  )
}
