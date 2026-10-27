import { cn } from '@/lib/cn'
import { CheckIcon } from '@/components/ui/icons'

export type Step = { label: string; done: boolean }

/** Frame 19: Pick list → Verify contacts → Prepare letter → Send → Track replies. */
export function StepIndicator({ steps }: { steps: Step[] }) {
  const current = steps.findIndex((s) => !s.done)
  return (
    <ol aria-label="Invitation workflow" className="flex flex-wrap items-center gap-x-2 gap-y-2">
      {steps.map((step, i) => {
        const isCurrent = i === current || (current === -1 && i === steps.length - 1)
        return (
          <li key={step.label} className="flex items-center gap-2">
            <span
              className={cn(
                'inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-medium',
                step.done
                  ? 'bg-green-50 text-green-700 dark:bg-green-500/10 dark:text-green-400'
                  : isCurrent
                    ? 'bg-brand-50 text-brand-700 ring-1 ring-brand-600 ring-inset dark:bg-brand-300/10 dark:text-brand-300 dark:ring-brand-300'
                    : 'bg-gray-100 text-gray-500 dark:bg-white/5 dark:text-gray-400',
              )}
            >
              {step.done ? <CheckIcon size={14} /> : <span aria-hidden>{i + 1}.</span>}
              {step.label}
            </span>
            {i < steps.length - 1 ? <span aria-hidden className="h-px w-4 bg-gray-300 dark:bg-white/15" /> : null}
          </li>
        )
      })}
    </ol>
  )
}
