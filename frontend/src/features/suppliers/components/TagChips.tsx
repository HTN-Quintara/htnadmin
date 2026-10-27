import { supplierRatingTags, type SupplierRatingTag } from '../schemas'
import { ratingTagLabel } from './labels'
import { cn } from '@/lib/cn'

type Props = {
  value: SupplierRatingTag[]
  onChange: (tags: SupplierRatingTag[]) => void
  'aria-label': string
  disabled?: boolean
}

/** Multi-select tag chips (On time / Good quality / Easy to reach / Fair price / Late / Below expectations). */
export function TagChips({ value, onChange, disabled, ...aria }: Props) {
  function toggle(tag: SupplierRatingTag) {
    onChange(value.includes(tag) ? value.filter((t) => t !== tag) : [...value, tag])
  }

  return (
    <div role="group" aria-label={aria['aria-label']} className="flex flex-wrap gap-2">
      {supplierRatingTags.map((tag) => {
        const checked = value.includes(tag)
        return (
          <label
            key={tag}
            className={cn(
              'inline-flex min-h-9 cursor-pointer items-center gap-1.5 rounded-md px-3 text-[0.8125rem] font-medium ring-1 ring-inset transition-colors',
              disabled && 'cursor-not-allowed opacity-50',
              checked
                ? 'bg-brand-50 text-brand-700 ring-brand-600 dark:bg-brand-300/10 dark:text-brand-300 dark:ring-brand-300'
                : 'bg-white text-gray-700 ring-gray-300 hover:bg-gray-50 dark:bg-gray-900 dark:text-gray-200 dark:ring-white/15 dark:hover:bg-gray-800',
            )}
          >
            <input type="checkbox" checked={checked} disabled={disabled} onChange={() => toggle(tag)} className="sr-only" />
            {ratingTagLabel(tag)}
          </label>
        )
      })}
    </div>
  )
}
