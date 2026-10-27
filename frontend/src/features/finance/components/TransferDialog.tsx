import { useId } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { PrefixInput } from '@/components/ui/PrefixInput'
import { useToast } from '@/components/ui/toast-context'
import { useFundsQuery, useTransferMutation } from '../api'
import { transferFormSchema, type TransferFormBody, type TransferFormInput } from '../schemas'

/** Moving money between funds. Recorded on both sides, which is why it asks for two funds and one amount. */
export function TransferDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const funds = useFundsQuery()
  const transfer = useTransferMutation()

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<TransferFormInput, unknown, TransferFormBody>({
    resolver: zodResolver(transferFormSchema),
    defaultValues: { fromFundId: '', toFundId: '', entryDate: today(), amount: '', description: '' },
  })

  const close = () => {
    reset()
    onClose()
  }

  const onSubmit = handleSubmit((body) => {
    if (transfer.isPending) return
    transfer.mutate(body, {
      onSuccess: () => {
        toast({ tone: 'success', title: 'Transfer recorded on both funds.' })
        close()
      },
      onError: (e) => {
        if (isApiError(e)) {
          for (const [field, message] of Object.entries(e.fieldErrors))
            setError(field as keyof TransferFormInput, { type: 'server', message })
          if (Object.keys(e.fieldErrors).length > 0) return
        }
        toast({ tone: 'error', title: 'Could not record the transfer.', description: errorMessage(e) })
      },
    })
  })

  const open_ = (funds.data ?? []).filter((f) => !f.isClosed)

  return (
    <Dialog
      open={open}
      onClose={close}
      title="Transfer between funds"
      description="Both sides are recorded, so nothing appears or disappears overall."
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={transfer.isPending}>
            Record transfer
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        <Field label="From" error={errors.fromFundId?.message}>
          {(p) => (
            <Select {...p} {...register('fromFundId')}>
              <option value="">Choose a fund</option>
              {open_.map((f) => (
                <option key={f.id} value={f.id}>
                  {f.name}
                </option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="To" error={errors.toFundId?.message}>
          {(p) => (
            <Select {...p} {...register('toFundId')}>
              <option value="">Choose a fund</option>
              {open_.map((f) => (
                <option key={f.id} value={f.id}>
                  {f.name}
                </option>
              ))}
            </Select>
          )}
        </Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Date" error={errors.entryDate?.message}>
            {(p) => <Input {...p} type="date" {...register('entryDate')} />}
          </Field>
          <Field label="Amount (₱)" error={errors.amount?.message}>
            {(p) => (
              <PrefixInput {...p} prefix="₱" inputMode="decimal" className="tabular-nums" {...register('amount')} />
            )}
          </Field>
        </div>
        <Field label="What it is for" error={errors.description?.message}>
          {(p) => <Textarea {...p} rows={2} {...register('description')} />}
        </Field>
      </form>
    </Dialog>
  )
}

function today(): string {
  return new Date(Date.now() + 8 * 60 * 60 * 1000).toISOString().slice(0, 10)
}
