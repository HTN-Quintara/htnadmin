import { formatPeso } from '@/lib/format'
import { cn } from '@/lib/cn'
import type { PopUpAllocation } from '../schemas'

export type PopUpProduct = PopUpAllocation & { price: number }

type Props = {
  products: PopUpProduct[]
  onAdd: (product: PopUpProduct) => void
}

/** Pop-up POS product grid (Phase 8C): one tile per allocated variant, scoped to what was brought to this pop-up. */
export function PopUpProductGrid({ products, onAdd }: Props) {
  if (products.length === 0) {
    return <p className="rounded-md bg-gray-50 px-4 py-8 text-center text-sm text-gray-600 dark:bg-white/5 dark:text-gray-400">No stock was allocated to this pop-up.</p>
  }

  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
      {products.map((p) => {
        const remaining = Math.max(0, p.qtyBrought - p.qtySold)
        return (
          <button
            key={p.variantId}
            type="button"
            disabled={remaining === 0}
            aria-label={`Add ${p.itemName} ${p.variantLabel} to the cart${remaining === 0 ? ' (out of stock)' : ` (${remaining} left)`}`}
            onClick={() => onAdd(p)}
            className={cn(
              'flex min-h-20 flex-col items-center justify-center gap-1 rounded-lg px-3 py-3 text-center ring-1 ring-inset transition-colors',
              remaining === 0
                ? 'cursor-not-allowed bg-gray-50 text-gray-400 ring-gray-200 dark:bg-white/5 dark:text-gray-600 dark:ring-white/10'
                : 'bg-white text-gray-900 ring-gray-300 hover:bg-brand-50 hover:ring-brand-600 dark:bg-gray-800 dark:text-gray-50 dark:ring-white/15 dark:hover:bg-brand-300/10',
            )}
          >
            <span className="text-sm font-semibold">
              {p.itemName} ({p.variantLabel})
            </span>
            <span className="text-xs text-gray-600 dark:text-gray-400">{formatPeso(p.price)}</span>
            <span className={cn('text-xs', remaining <= 5 && remaining > 0 ? 'font-semibold text-red-700 dark:text-red-400' : 'text-gray-500 dark:text-gray-400')}>
              {remaining === 0 ? 'Out of stock' : `${remaining} left`}
            </span>
          </button>
        )
      })}
    </div>
  )
}
