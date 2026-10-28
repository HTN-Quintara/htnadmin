import { formatPeso } from '@/lib/format'
import { cn } from '@/lib/cn'
import type { MerchItem, MerchVariant } from '../schemas'

export type MembershipTileProps = {
  year: number
  fee: number
  included: boolean
  onToggle: () => void
}

type Props = {
  items: MerchItem[]
  onAdd: (item: MerchItem, variant: MerchVariant) => void
  /** Phase 8B: the "Membership {year}" tile, shown only when this event is the current main event with a fee. */
  membership?: MembershipTileProps | null
}

/** Product grid (canvas frame 21, left column): one card per item, variants as pill buttons. */
export function ProductGrid({ items, onAdd, membership }: Props) {
  const active = items.filter((i) => i.isActive)
  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
      {membership ? (
        <div className="flex flex-col gap-3 rounded-lg bg-white p-4 shadow-sm ring-1 ring-gray-900/5 dark:bg-gray-900 dark:ring-white/10">
          <div className="flex items-center gap-3">
            <span
              aria-hidden="true"
              className="flex size-10 shrink-0 items-center justify-center rounded-md bg-brand-600 text-sm font-semibold text-white"
            >
              M
            </span>
            <div className="flex min-w-0 flex-col">
              <span className="truncate font-semibold text-gray-900 dark:text-gray-50">Membership {membership.year}</span>
              <span className="text-sm text-gray-600 dark:text-gray-400">{formatPeso(membership.fee)} yearly fee</span>
            </div>
          </div>
          <button
            type="button"
            role="switch"
            aria-checked={membership.included}
            aria-label={`${membership.included ? 'Remove' : 'Add'} Membership ${membership.year} ${membership.included ? 'from' : 'to'} the sale`}
            onClick={membership.onToggle}
            className={cn(
              'flex min-h-11 items-center justify-center rounded-md px-3 py-1.5 text-sm font-medium ring-1 ring-inset transition-colors',
              membership.included
                ? 'bg-brand-600 text-white ring-brand-600 hover:bg-brand-700'
                : 'bg-white text-gray-900 ring-gray-300 hover:bg-brand-50 hover:ring-brand-600 dark:bg-gray-800 dark:text-gray-50 dark:ring-white/15 dark:hover:bg-brand-300/10',
            )}
          >
            {membership.included ? 'Added to sale ✓' : 'Add to sale'}
          </button>
        </div>
      ) : null}
      {active.map((item) => (
        <div key={item.id} className="flex flex-col gap-3 rounded-lg bg-white p-4 shadow-sm ring-1 ring-gray-900/5 dark:bg-gray-900 dark:ring-white/10">
          <div className="flex items-center gap-3">
            <span
              aria-hidden="true"
              className="size-10 shrink-0 rounded-md ring-1 ring-gray-900/10 dark:ring-white/15"
              style={{ backgroundColor: item.swatchColor ?? '#9CA3AF' }}
            />
            <div className="flex min-w-0 flex-col">
              <span className="truncate font-semibold text-gray-900 dark:text-gray-50">{item.name}</span>
              <span className="text-sm text-gray-600 dark:text-gray-400">{formatPeso(item.price)}</span>
            </div>
          </div>
          <div className="flex flex-wrap gap-2">
            {item.variants
              .filter((v) => v.isActive)
              .map((v) => (
                <button
                  key={v.id}
                  type="button"
                  disabled={v.stockOnHand === 0}
                  aria-label={`Add ${item.name} ${v.label} to the cart${v.stockOnHand === 0 ? ' (out of stock)' : ` (${v.stockOnHand} left)`}`}
                  onClick={() => onAdd(item, v)}
                  className={cn(
                    'flex min-h-11 min-w-16 flex-col items-center justify-center gap-0.5 rounded-md px-3 py-1.5 text-sm font-medium ring-1 ring-inset transition-colors',
                    v.stockOnHand === 0
                      ? 'cursor-not-allowed bg-gray-50 text-gray-400 ring-gray-200 dark:bg-white/5 dark:text-gray-600 dark:ring-white/10'
                      : 'bg-white text-gray-900 ring-gray-300 hover:bg-brand-50 hover:ring-brand-600 dark:bg-gray-800 dark:text-gray-50 dark:ring-white/15 dark:hover:bg-brand-300/10',
                  )}
                >
                  <span>{v.label}</span>
                  <span className={cn('text-xs', v.stockOnHand <= 5 && v.stockOnHand > 0 ? 'font-semibold text-red-700 dark:text-red-400' : 'text-gray-500 dark:text-gray-400')}>
                    {v.stockOnHand === 0 ? 'Out of stock' : `${v.stockOnHand} left`}
                  </span>
                </button>
              ))}
          </div>
        </div>
      ))}
    </div>
  )
}
