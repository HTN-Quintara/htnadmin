import { StarIcon } from '@/components/ui/icons'
import { cn } from '@/lib/cn'

type Props = {
  name: string
  value: number
  onChange: (stars: number) => void
  'aria-label': string
  disabled?: boolean
}

const STARS = [1, 2, 3, 4, 5]

/** Real `<input type="radio">` 1–5 star picker (accessibility: a div with click handlers would not announce as a form control). */
export function StarRating({ name, value, onChange, disabled, ...aria }: Props) {
  return (
    <div role="radiogroup" aria-label={aria['aria-label']} className="flex items-center gap-1">
      {STARS.map((n) => {
        const checked = value === n
        return (
          <label
            key={n}
            className={cn(
              'flex size-11 cursor-pointer items-center justify-center rounded-md',
              disabled ? 'cursor-not-allowed opacity-50' : 'hover:bg-gray-100 dark:hover:bg-white/5',
            )}
          >
            <input
              type="radio"
              name={name}
              value={n}
              checked={checked}
              disabled={disabled}
              onChange={() => onChange(n)}
              className="sr-only"
            />
            <StarIcon
              size={26}
              fill={n <= value ? 'currentColor' : 'none'}
              className={n <= value ? 'text-brand-600 dark:text-brand-300' : 'text-gray-300 dark:text-gray-600'}
            />
            <span className="sr-only">{n} star{n === 1 ? '' : 's'}</span>
          </label>
        )
      })}
    </div>
  )
}
