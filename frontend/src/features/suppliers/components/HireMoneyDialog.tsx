import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input, Textarea } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useUpdateHireMutation } from '../api'
import { hireMoneyFormSchema, type EventSupplier, type HireMoneyFormValues } from '../schemas'

type Props = { open: boolean; onClose: () => void; eventId: string; hire: EventSupplier | null }

function toNumber(s: string | undefined): number | null {
  if (!s || s.trim() === '') return null
  const n = Number(s)
  return Number.isFinite(n) ? n : null
}

/** Edits a hire's tracking money figures and note. Status moves separately, one step at a time (see HiresTable). */
export function HireMoneyDialog({ open, onClose, eventId, hire }: Props) {
  const update = useUpdateHireMutation(eventId)
  const { toast } = useToast()
  const form = useForm<HireMoneyFormValues>({ resolver: zodResolver(hireMoneyFormSchema), defaultValues: defaults(hire) })
  const { register, handleSubmit, reset, setError, formState } = form

  useEffect(() => { if (open) reset(defaults(hire)) }, [open, hire, reset])

  function onSubmit(values: HireMoneyFormValues) {
    if (!hire || update.isPending) return
    update.mutate(
      {
        hireId: hire.id,
        body: {
          contractAmount: toNumber(values.contractAmount),
          paidAmount: toNumber(values.paidAmount),
          nextPaymentAmount: toNumber(values.nextPaymentAmount),
          nextPaymentDue: values.nextPaymentDue || null,
          note: values.note || null,
        },
      },
      {
        onSuccess: () => { toast({ tone: 'success', title: 'Hire updated.' }); onClose() },
        onError: (e) => setError('contractAmount', { message: errorMessage(e) }),
      },
    )
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Edit hire"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Cancel</Button>
          <Button form="hire-money-form" type="submit" loading={update.isPending}>Save</Button>
        </>
      }
    >
      <form id="hire-money-form" className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
        <Field label="Contract amount (₱)" optional error={formState.errors.contractAmount?.message}>
          {(c) => <Input {...c} inputMode="decimal" {...register('contractAmount')} />}
        </Field>
        <Field label="Paid amount (₱)" optional>
          {(c) => <Input {...c} inputMode="decimal" {...register('paidAmount')} />}
        </Field>
        <Field label="Next payment amount (₱)" optional>
          {(c) => <Input {...c} inputMode="decimal" {...register('nextPaymentAmount')} />}
        </Field>
        <Field label="Next payment due" optional>
          {(c) => <Input type="date" {...c} {...register('nextPaymentDue')} />}
        </Field>
        <Field label="Note" optional>
          {(c) => <Textarea {...c} rows={2} {...register('note')} />}
        </Field>
      </form>
    </Dialog>
  )
}

function defaults(h: EventSupplier | null): HireMoneyFormValues {
  return {
    contractAmount: h?.money?.contractAmount != null ? String(h.money.contractAmount) : '',
    paidAmount: h?.money?.paidAmount != null ? String(h.money.paidAmount) : '',
    nextPaymentAmount: h?.money?.nextPaymentAmount != null ? String(h.money.nextPaymentAmount) : '',
    nextPaymentDue: h?.money?.nextPaymentDue ?? '',
    note: h?.note ?? '',
  }
}
