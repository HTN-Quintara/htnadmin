import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import type { MonthStatus } from '../schemas'

/**
 * Closing a month. When it cannot be closed yet the dialog says exactly why, in the same words the server used —
 * "you can't do this" with no reason is the one thing a month-end screen must never do.
 */
export function CloseMonthDialog({
  period,
  open,
  onClose,
  onConfirm,
  pending,
}: {
  period: MonthStatus
  open: boolean
  onClose: () => void
  onConfirm: () => void
  pending?: boolean
}) {
  const blocked = period.blockers.length > 0

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={`Close ${period.label}`}
      role="alertdialog"
      size="sm"
      description={
        blocked
          ? undefined
          : 'Every entry in the month is frozen once it closes. A later correction is a new entry dated today.'
      }
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {blocked ? 'Close' : 'Cancel'}
          </Button>
          {blocked ? null : (
            <Button onClick={onConfirm} loading={pending}>
              Close the month
            </Button>
          )}
        </>
      }
    >
      {blocked ? (
        <div className="flex flex-col gap-3">
          <Alert tone="yellow" live="status">
            {period.label} cannot be closed yet.
          </Alert>
          <ul className="flex list-disc flex-col gap-1 pl-5 text-sm text-gray-700 dark:text-gray-300">
            {period.blockers.map((blocker) => (
              <li key={blocker.code}>{blocker.message}</li>
            ))}
          </ul>
        </div>
      ) : (
        <p className="text-sm text-gray-700 dark:text-gray-300">
          Nothing is pending and every account is balanced for {period.label}.
        </p>
      )}
    </Dialog>
  )
}
