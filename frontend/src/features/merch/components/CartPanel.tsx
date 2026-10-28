import { Button } from '@/components/ui/Button'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { formatPeso } from '@/lib/format'
import type { MerchSaleMethod } from '../schemas'

export type CartLine = {
  itemId: string
  itemName: string
  variantId: string
  variantLabel: string
  unitPrice: number
  qty: number
}

type Props = {
  lines: CartLine[]
  onQtyChange: (variantId: string, qty: number) => void
  subtotal: number
  discountLabel: string | null
  discountAmount: number | null
  total: number
  method: MerchSaleMethod
  onMethodChange: (m: MerchSaleMethod) => void
  hasBuyer: boolean
  onSubmit: () => void
  submitting: boolean
}

const methodOptions: Array<{ value: MerchSaleMethod; label: string }> = [
  { value: 'Cash', label: 'Cash' },
  { value: 'GCash', label: 'GCash' },
  { value: 'Bank', label: 'Bank transfer' },
]

/** Cart + totals + payment method + submit (canvas frame 21, "Current sale"). */
export function CartPanel({ lines, onQtyChange, subtotal, discountLabel, discountAmount, total, method, onMethodChange, hasBuyer, onSubmit, submitting }: Props) {
  const hasItems = lines.length > 0
  const hint = !hasBuyer
    ? 'Identify the buyer first so the sale is tied to a person.'
    : !hasItems
      ? 'Add at least one item.'
      : 'Stock updates as soon as the sale is completed.'

  return (
    <div className="flex flex-col gap-4">
      {hasItems ? (
        <ul className="flex flex-col gap-2">
          {lines.map((line) => (
            <li key={line.variantId} className="flex items-center gap-3">
              <div className="flex min-w-0 flex-1 flex-col">
                <span className="truncate text-sm font-medium text-gray-900 dark:text-gray-50">
                  {line.itemName} ({line.variantLabel})
                </span>
                <span className="text-xs text-gray-600 dark:text-gray-400">{formatPeso(line.unitPrice)} each</span>
              </div>
              <div className="flex items-center gap-1.5">
                <button
                  type="button"
                  aria-label={`Decrease ${line.itemName} ${line.variantLabel}`}
                  onClick={() => onQtyChange(line.variantId, line.qty - 1)}
                  className="inline-flex size-11 shrink-0 items-center justify-center rounded-md text-gray-700 ring-1 ring-inset ring-gray-300 hover:bg-gray-50 dark:text-gray-200 dark:ring-white/15 dark:hover:bg-gray-800"
                >
                  −
                </button>
                <span className="w-6 text-center text-sm tabular-nums">{line.qty}</span>
                <button
                  type="button"
                  aria-label={`Increase ${line.itemName} ${line.variantLabel}`}
                  onClick={() => onQtyChange(line.variantId, line.qty + 1)}
                  className="inline-flex size-11 shrink-0 items-center justify-center rounded-md text-gray-700 ring-1 ring-inset ring-gray-300 hover:bg-gray-50 dark:text-gray-200 dark:ring-white/15 dark:hover:bg-gray-800"
                >
                  +
                </button>
              </div>
              <span className="w-20 text-right text-sm font-medium tabular-nums text-gray-900 dark:text-gray-50">
                {formatPeso(line.unitPrice * line.qty)}
              </span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="rounded-md bg-gray-50 px-3 py-6 text-center text-sm text-gray-600 dark:bg-white/5 dark:text-gray-400">
          Tap a size on the left to add an item
        </p>
      )}

      <div className="flex flex-col gap-1.5 border-t border-gray-100 pt-3 text-sm dark:border-white/10">
        <div className="flex items-center justify-between text-gray-700 dark:text-gray-300">
          <span>Subtotal</span>
          <span className="tabular-nums">{formatPeso(subtotal)}</span>
        </div>
        {discountAmount != null && discountAmount > 0 ? (
          <div className="flex items-center justify-between font-medium text-green-700 dark:text-green-400">
            <span>{discountLabel ?? 'Discount'}</span>
            <span className="tabular-nums">−{formatPeso(discountAmount)}</span>
          </div>
        ) : null}
        <div className="flex items-center justify-between text-base font-semibold text-gray-900 dark:text-gray-50">
          <span>Total</span>
          <span className="tabular-nums">{formatPeso(total)}</span>
        </div>
      </div>

      <div className="flex flex-col gap-2">
        <span id="pos-method-label" className="text-sm font-medium text-gray-700 dark:text-gray-200">
          Payment method
        </span>
        <SegmentedControl
          options={methodOptions}
          value={method}
          onChange={onMethodChange}
          variant="chips"
          size="lg"
          fill
          aria-labelledby="pos-method-label"
        />
      </div>

      <Button block size="lg" disabled={!hasBuyer || !hasItems} loading={submitting} onClick={onSubmit}>
        Complete sale · {formatPeso(total)}
      </Button>
      <p className="text-center text-xs text-gray-600 dark:text-gray-400">{hint}</p>
    </div>
  )
}
