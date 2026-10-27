import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Select, Textarea } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useCommitteesQuery } from '@/features/committees/api'
import { useCategoriesQuery } from '@/features/finance/api'
import { useCreateHireMutation, useSuppliers } from '../api'
import { hireFormSchema, type HireFormValues } from '../schemas'
import { categoryLabel } from './labels'

type Props = { open: boolean; onClose: () => void; eventId: string }

/** Hire a supplier for this event (frame 25's "suppliers for the event", the create side of EventSupplier). */
export function HireFormDialog({ open, onClose, eventId }: Props) {
  const suppliers = useSuppliers()
  const categories = useCategoriesQuery()
  const committees = useCommitteesQuery(eventId)
  const create = useCreateHireMutation(eventId)
  const { toast } = useToast()

  const form = useForm<HireFormValues>({
    resolver: zodResolver(hireFormSchema),
    defaultValues: { supplierId: '', categoryId: '', ownerCommitteeId: '', note: '' },
  })
  const { register, handleSubmit, reset, setError, formState } = form

  useEffect(() => { if (open) reset({ supplierId: '', categoryId: '', ownerCommitteeId: '', note: '' }) }, [open, reset])

  const activeSuppliers = (suppliers.data ?? []).filter((s) => s.isActive)
  const activeCategories = (categories.data ?? []).filter((c) => c.isActive)
  const committeeOptions = committees.data?.committees.filter((c) => c.isActive) ?? []

  function onSubmit(values: HireFormValues) {
    if (create.isPending) return
    create.mutate(
      { supplierId: values.supplierId, categoryId: values.categoryId, ownerCommitteeId: values.ownerCommitteeId, note: values.note || null },
      {
        onSuccess: () => { toast({ tone: 'success', title: 'Supplier hired.' }); onClose() },
        onError: (e) => {
          if (isApiError(e) && e.code === 'already_hired') {
            setError('supplierId', { message: errorMessage(e) })
            return
          }
          setError('supplierId', { message: errorMessage(e) })
        },
      },
    )
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Hire a supplier"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Cancel</Button>
          <Button form="hire-form" type="submit" loading={create.isPending}>Hire supplier</Button>
        </>
      }
    >
      <form id="hire-form" className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
        <Field label="Supplier" error={formState.errors.supplierId?.message}>
          {(c) => (
            <Select {...c} {...register('supplierId')} aria-invalid={formState.errors.supplierId ? true : undefined}>
              <option value="">Pick a supplier…</option>
              {activeSuppliers.map((s) => (
                <option key={s.id} value={s.id}>{s.name} ({categoryLabel(s.category)})</option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Budget category" error={formState.errors.categoryId?.message}>
          {(c) => (
            <Select {...c} {...register('categoryId')} aria-invalid={formState.errors.categoryId ? true : undefined}>
              <option value="">Pick a category…</option>
              {activeCategories.map((cat) => (
                <option key={cat.id} value={cat.id}>{cat.name}</option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Owning committee" error={formState.errors.ownerCommitteeId?.message} hint="The lead of this committee can rate this hire after the event.">
          {(c) => (
            <Select {...c} {...register('ownerCommitteeId')} aria-invalid={formState.errors.ownerCommitteeId ? true : undefined}>
              <option value="">Pick a committee…</option>
              {committeeOptions.map((com) => (
                <option key={com.id} value={com.id}>{com.name}</option>
              ))}
            </Select>
          )}
        </Field>
        <Field label="Note" optional>
          {(c) => <Textarea {...c} rows={2} {...register('note')} />}
        </Field>
      </form>
    </Dialog>
  )
}
