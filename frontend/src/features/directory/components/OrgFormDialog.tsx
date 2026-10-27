import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { FileButton } from '@/components/ui/FileButton'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { UploadIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { useCreateOrgMutation, useUpdateOrgMutation, useUploadOrgLogoMutation } from '../api'
import { orgFormSchema, type DirectoryOrg, type OrgFormValues } from '../schemas'
import { orgKindLabel } from './labels'

type Props = { open: boolean; onClose: () => void; org?: DirectoryOrg }

const LOGO_MAX_BYTES = 1024 * 1024

function checkLogo(file: File): string | null {
  const okType = file.type === 'image/png' || file.type === 'image/jpeg' || /\.(png|jpe?g)$/i.test(file.name)
  if (!okType) return 'Upload a PNG or JPG image.'
  if (file.size > LOGO_MAX_BYTES) return 'The logo is larger than 1 MB.'
  return null
}

/** Create/edit an organization. The API only lets the addressee, website and notes change after creation. */
export function OrgFormDialog({ open, onClose, org }: Props) {
  const create = useCreateOrgMutation()
  const update = useUpdateOrgMutation()
  const upload = useUploadOrgLogoMutation()
  const { toast } = useToast()
  const [logoError, setLogoError] = useState<string | null>(null)
  const saving = create.isPending || update.isPending

  const form = useForm<OrgFormValues>({ resolver: zodResolver(orgFormSchema), defaultValues: defaults(org) })
  const { register, handleSubmit, reset, setError, formState } = form

  useEffect(() => { if (open) reset(defaults(org)) }, [open, org, reset])

  function onSubmit(values: OrgFormValues) {
    if (saving) return
    if (org) {
      update.mutate(
        { id: org.id, body: { addresseeSalutation: values.addresseeSalutation || null, websiteUrl: values.websiteUrl || null, notes: values.notes || null } },
        {
          onSuccess: () => { toast({ tone: 'success', title: 'Organization updated.' }); onClose() },
          onError: (e) => setError('addresseeSalutation', { message: errorMessage(e) }),
        },
      )
      return
    }
    create.mutate(
      { kind: values.kind, name: values.name, addresseeSalutation: values.addresseeSalutation || null, websiteUrl: values.websiteUrl || null, notes: values.notes || null },
      {
        onSuccess: () => { toast({ tone: 'success', title: 'Organization added.' }); onClose() },
        onError: (e) => {
          if (isApiError(e) && e.code === 'duplicate_name') {
            setError('name', { message: 'An organization with that name already exists.' })
            return
          }
          const fields = isApiError(e) ? e.fieldErrors : {}
          for (const [field, message] of Object.entries(fields)) setError(field as keyof OrgFormValues, { message })
          if (!Object.keys(fields).length) setError('name', { message: errorMessage(e) })
        },
      },
    )
  }

  function onLogoFile(file: File) {
    if (!org) return
    const problem = checkLogo(file)
    setLogoError(problem)
    if (problem) return
    upload.mutate(
      { id: org.id, file },
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
      title={org ? 'Edit organization' : 'Add organization'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Cancel</Button>
          <Button form="org-form" type="submit" loading={saving}>{org ? 'Save' : 'Add organization'}</Button>
        </>
      }
    >
      <form id="org-form" className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
        <Field label="Kind">
          {(c) =>
            org ? (
              <Input {...c} readOnly value={orgKindLabel(org.kind)} />
            ) : (
              <Select {...c} {...register('kind')}>
                <option value="Government">Government</option>
                <option value="Company">Company</option>
                <option value="University">University</option>
                <option value="NonProfit">Non-profit</option>
              </Select>
            )
          }
        </Field>
        <Field label="Name" error={formState.errors.name?.message} hint={org ? 'The name cannot change after creation.' : undefined}>
          {(c) => <Input {...c} {...register('name')} readOnly={!!org} aria-invalid={formState.errors.name ? true : undefined} />}
        </Field>
        <Field label="Letter addressee" optional hint="e.g. “The Secretary”, “The Chancellor”.">
          {(c) => <Input {...c} {...register('addresseeSalutation')} />}
        </Field>
        <Field label="Website" optional error={formState.errors.websiteUrl?.message}>
          {(c) => <Input {...c} {...register('websiteUrl')} placeholder="https://" />}
        </Field>
        <Field label="Notes" optional>
          {(c) => <Textarea {...c} rows={3} {...register('notes')} />}
        </Field>
        {org ? (
          <div className="flex flex-col gap-2">
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Logo</span>
            <div className="flex items-center gap-3">
              {org.logoFileId ? (
                <img src={`/api/files/${org.logoFileId}`} alt={`${org.name} logo`} className="size-10 rounded-md object-contain ring-1 ring-gray-200 dark:ring-white/10" />
              ) : null}
              <FileButton accept="image/png,image/jpeg,.png,.jpg,.jpeg" onFile={onLogoFile} loading={upload.isPending} size="sm">
                <UploadIcon size={14} />
                {org.logoFileId ? 'Replace' : 'Upload'}
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

function defaults(o?: DirectoryOrg): OrgFormValues {
  return {
    kind: o?.kind ?? 'Government',
    name: o?.name ?? '',
    addresseeSalutation: o?.addresseeSalutation ?? '',
    websiteUrl: o?.websiteUrl ?? '',
    notes: o?.notes ?? '',
  }
}
