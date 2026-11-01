import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { FileButton } from '@/components/ui/FileButton'
import { Checkbox, Input, Select, Textarea } from '@/components/ui/Input'
import { UploadIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { useCreateSupplierMutation, useUpdateSupplierMutation, useUploadSupplierLogoMutation } from '../api'
import { supplierCategories, supplierFormSchema, type Supplier, type SupplierFormValues } from '../schemas'
import { categoryLabel } from './labels'

type Props = { open: boolean; onClose: () => void; supplier?: Supplier }

const LOGO_MAX_BYTES = 1024 * 1024

function checkLogo(file: File): string | null {
  const okType = file.type === 'image/png' || file.type === 'image/jpeg' || /\.(png|jpe?g)$/i.test(file.name)
  if (!okType) return 'Upload a PNG or JPG image.'
  if (file.size > LOGO_MAX_BYTES) return 'The logo is larger than 1 MB.'
  return null
}

/** Create/edit a supplier (frame 18b). Category and name are frozen once created, like a Directory org. */
export function SupplierFormDialog({ open, onClose, supplier }: Props) {
  const create = useCreateSupplierMutation()
  const update = useUpdateSupplierMutation()
  const upload = useUploadSupplierLogoMutation()
  const { toast } = useToast()
  const [logoError, setLogoError] = useState<string | null>(null)
  const saving = create.isPending || update.isPending

  const form = useForm<SupplierFormValues>({ resolver: zodResolver(supplierFormSchema), defaultValues: defaults(supplier) })
  const { register, handleSubmit, reset, setError, formState } = form

  useEffect(() => { if (open) reset(defaults(supplier)) }, [open, supplier, reset])

  function onSubmit(values: SupplierFormValues) {
    if (saving) return
    const editable = {
      contactName: values.contactName || null,
      contactEmail: values.contactEmail || null,
      contactPhone: values.contactPhone || null,
      issuesOr: values.issuesOr,
      tin: values.tin || null,
      bankDetails: values.bankDetails || null,
      notes: values.notes || null,
    }
    if (supplier) {
      update.mutate(
        { id: supplier.id, body: editable },
        {
          onSuccess: () => { toast({ tone: 'success', title: 'Supplier updated.' }); onClose() },
          onError: (e) => setError('notes', { message: errorMessage(e) }),
        },
      )
      return
    }
    create.mutate(
      { category: values.category, name: values.name, ...editable },
      {
        onSuccess: () => { toast({ tone: 'success', title: 'Supplier added.' }); onClose() },
        onError: (e) => {
          if (isApiError(e) && e.code === 'duplicate_name') {
            setError('name', { message: 'A supplier with that name already exists.' })
            return
          }
          const fields = isApiError(e) ? e.fieldErrors : {}
          for (const [field, message] of Object.entries(fields)) setError(field as keyof SupplierFormValues, { message })
          if (!Object.keys(fields).length) setError('name', { message: errorMessage(e) })
        },
      },
    )
  }

  function onLogoFile(file: File) {
    if (!supplier) return
    const problem = checkLogo(file)
    setLogoError(problem)
    if (problem) return
    upload.mutate(
      { id: supplier.id, file },
      {
        onSuccess: () => toast({ tone: 'success', title: 'Logo uploaded.' }),
        onError: (e) => setLogoError(errorMessage(e, { 400: 'Upload a PNG or JPG up to 1 MB.' })),
      },
    )
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={supplier ? 'Edit supplier' : 'Add supplier'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Cancel</Button>
          <Button form="supplier-form" type="submit" loading={saving}>{supplier ? 'Save' : 'Add supplier'}</Button>
        </>
      }
    >
      <form id="supplier-form" className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
        <Field label="Category">
          {(c) =>
            supplier ? (
              <Input {...c} readOnly value={categoryLabel(supplier.category)} />
            ) : (
              <Select {...c} {...register('category')}>
                {supplierCategories.map((cat) => (
                  <option key={cat} value={cat}>{categoryLabel(cat)}</option>
                ))}
              </Select>
            )
          }
        </Field>
        <Field label="Name" error={formState.errors.name?.message} hint={supplier ? 'The name cannot change after creation.' : undefined}>
          {(c) => <Input {...c} {...register('name')} readOnly={!!supplier} aria-invalid={formState.errors.name ? true : undefined} />}
        </Field>
        <Field label="Contact name" optional>
          {(c) => <Input {...c} {...register('contactName')} />}
        </Field>
        <Field label="Contact email" optional error={formState.errors.contactEmail?.message}>
          {(c) => <Input type="email" {...c} {...register('contactEmail')} />}
        </Field>
        <Field label="Contact phone" optional>
          {(c) => <Input {...c} {...register('contactPhone')} />}
        </Field>
        <label className="flex items-center gap-2 text-sm">
          <Checkbox {...register('issuesOr')} /> Issues an official receipt
        </label>
        <Field label="TIN" optional hint="Visible to the board only.">
          {(c) => <Input {...c} {...register('tin')} />}
        </Field>
        <Field label="Bank details" optional hint="Visible to the board only.">
          {(c) => <Textarea {...c} rows={2} {...register('bankDetails')} />}
        </Field>
        <Field label="Notes" optional>
          {(c) => <Textarea {...c} rows={3} {...register('notes')} />}
        </Field>
        {supplier ? (
          <div className="flex flex-col gap-2">
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Logo</span>
            <div className="flex items-center gap-3">
              {supplier.logoFileId ? (
                <img src={`/api/files/${supplier.logoFileId}`} alt={`${supplier.name} logo`} className="size-10 rounded-md object-contain ring-1 ring-gray-200 dark:ring-white/10" />
              ) : null}
              <FileButton accept="image/png,image/jpeg,.png,.jpg,.jpeg" onFile={onLogoFile} loading={upload.isPending} size="sm">
                <UploadIcon size={14} />
                {supplier.logoFileId ? 'Replace' : 'Upload'}
                <span className="sr-only"> logo</span>
              </FileButton>
            </div>
            {logoError ? <p role="alert" className="text-xs font-medium text-red-700 dark:text-red-400">{logoError}</p> : null}
          </div>
        ) : null}
      </form>
    </Dialog>
  )
}

function defaults(s?: Supplier): SupplierFormValues {
  return {
    category: s?.category ?? 'Venue',
    name: s?.name ?? '',
    contactName: s?.contactName ?? '',
    contactEmail: s?.contactEmail ?? '',
    contactPhone: s?.contactPhone ?? '',
    issuesOr: s?.issuesOr ?? false,
    tin: s?.boardOnly?.tin ?? '',
    bankDetails: s?.boardOnly?.bankDetails ?? '',
    notes: s?.notes ?? '',
  }
}
