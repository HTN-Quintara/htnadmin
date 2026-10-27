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
import { useCategoriesQuery, useCreateRequestMutation } from '../api'
import { requestFormSchema, type RequestFormBody, type RequestFormInput } from '../schemas'

/** Filing an expense request: anything the organization spends outside an event. */
export function RequestDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const formId = useId()
  const { toast } = useToast()
  const categories = useCategoriesQuery()
  const create = useCreateRequestMutation()

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<RequestFormInput, unknown, RequestFormBody>({
    resolver: zodResolver(requestFormSchema),
    defaultValues: { title: '', description: '', amount: '', categoryId: '', neededBy: '' },
  })

  const close = () => {
    reset()
    onClose()
  }

  const onSubmit = handleSubmit((body) => {
    if (create.isPending) return
    create.mutate(body, {
      onSuccess: () => {
        toast({ tone: 'success', title: 'Request filed. The CFO will decide on it.' })
        close()
      },
      onError: (e) => {
        if (isApiError(e)) {
          for (const [field, message] of Object.entries(e.fieldErrors))
            setError(field as keyof RequestFormInput, { type: 'server', message })
          if (Object.keys(e.fieldErrors).length > 0) return
        }
        toast({ tone: 'error', title: 'Could not file the request.', description: errorMessage(e) })
      },
    })
  })

  return (
    <Dialog
      open={open}
      onClose={close}
      title="New expense request"
      description="For spending that is not tied to an event."
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={create.isPending}>
            File request
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        <Field label="What is the money for?" error={errors.title?.message}>
          {(p) => <Input {...p} {...register('title')} />}
        </Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Amount (₱)" error={errors.amount?.message}>
            {(p) => (
              <PrefixInput {...p} prefix="₱" inputMode="decimal" className="tabular-nums" {...register('amount')} />
            )}
          </Field>
          <Field label="Category" error={errors.categoryId?.message}>
            {(p) => (
              <Select {...p} {...register('categoryId')}>
                <option value="">Choose a category</option>
                {(categories.data ?? []).map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            )}
          </Field>
          <Field label="Needed by" optional error={errors.neededBy?.message}>
            {(p) => <Input {...p} type="date" {...register('neededBy')} />}
          </Field>
        </div>
        <Field label="Details" optional error={errors.description?.message}>
          {(p) => <Textarea {...p} rows={3} {...register('description')} />}
        </Field>
      </form>
    </Dialog>
  )
}
