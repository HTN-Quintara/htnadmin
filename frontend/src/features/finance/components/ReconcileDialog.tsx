import { useId } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Textarea } from '@/components/ui/Input'
import { PrefixInput } from '@/components/ui/PrefixInput'
import { useToast } from '@/components/ui/toast-context'
import { formatPeso } from '@/lib/format'
import { useReconcileMutation, type PeriodFilter } from '../api'
import { reconcileFormSchema, type AccountBalance, type ReconcileFormBody, type ReconcileFormInput } from '../schemas'

/**
 * The book balance next to what the statement says. The book figure is shown but never editable — it is derived from
 * the posted entries, so the only thing to type is the real-world number it is being compared against.
 */
export function ReconcileDialog({
  account,
  period,
  open,
  onClose,
}: {
  account: AccountBalance | null
  period: PeriodFilter
  open: boolean
  onClose: () => void
}) {
  const formId = useId()
  const { toast } = useToast()
  const reconcile = useReconcileMutation(account?.id ?? '', period)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<ReconcileFormInput, unknown, ReconcileFormBody>({
    resolver: zodResolver(reconcileFormSchema),
    defaultValues: { statementBalance: '', note: '' },
  })

  const close = () => {
    reset()
    onClose()
  }

  const onSubmit = handleSubmit((body) => {
    if (reconcile.isPending || !account) return
    reconcile.mutate(body, {
      onSuccess: () => {
        const difference = body.statementBalance - account.bookBalance
        toast({
          tone: difference === 0 ? 'success' : 'info',
          title: difference === 0 ? `${account.label} is balanced.` : `${account.label} is off by ${formatPeso(Math.abs(difference))}.`,
        })
        close()
      },
      onError: (e) => {
        if (isApiError(e)) {
          for (const [field, message] of Object.entries(e.fieldErrors))
            setError(field as keyof ReconcileFormInput, { type: 'server', message })
          if (Object.keys(e.fieldErrors).length > 0) return
        }
        toast({ tone: 'error', title: 'Could not save the reconciliation.', description: errorMessage(e) })
      },
    })
  })

  return (
    <Dialog
      open={open}
      onClose={close}
      title={account ? `Reconcile ${account.label}` : 'Reconcile'}
      eyebrow={account?.kind === 'Cash' ? 'Counted cash' : 'Bank or GCash statement'}
      description={
        account
          ? `The books say ${formatPeso(account.bookBalance)} at the end of this month. Enter what the statement or the cash count says.`
          : undefined
      }
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={reconcile.isPending}>
            Save
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        <Field
          label="Statement balance (₱)"
          error={errors.statementBalance?.message}
          hint="Use a minus sign if the account is overdrawn."
        >
          {(p) => (
            <PrefixInput
              {...p}
              prefix="₱"
              inputMode="decimal"
              className="tabular-nums"
              {...register('statementBalance')}
            />
          )}
        </Field>
        <Field label="Note" optional error={errors.note?.message}>
          {(p) => <Textarea {...p} rows={2} {...register('note')} />}
        </Field>
      </form>
    </Dialog>
  )
}
