import { useEffect } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input, Select, Checkbox } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { parseAmount, parseInteger } from '@/lib/money'
import { useSaveSponsorMutation } from '../api'
import { sponsorFormSchema, type Sponsor, type SponsorFormValues, type SponsorTier } from '../schemas'

type Props = {
  eventId: string
  tiers: SponsorTier[]
  open: boolean
  onClose: () => void
  sponsor?: Sponsor
}

export function SponsorDialog({ eventId, tiers, open, onClose, sponsor }: Props) {
  const save = useSaveSponsorMutation(eventId)
  const { toast } = useToast()
  const form = useForm<SponsorFormValues>({
    resolver: zodResolver(sponsorFormSchema),
    defaultValues: defaults(sponsor),
  })
  const { register, handleSubmit, reset, control, setError, formState } = form
  const kind = useWatch({ control, name: 'kind' })
  const isAnonymous = useWatch({ control, name: 'isAnonymous' })

  useEffect(() => { if (open) reset(defaults(sponsor)) }, [open, sponsor, reset])

  function onSubmit(values: SponsorFormValues) {
    if (save.isPending) return
    const cash = values.cashAmount ? parseAmount(values.cashAmount) : null
    if (values.cashAmount && cash === null) {
      setError('cashAmount', { message: 'Enter an amount like 100000 or 100000.50.' })
      return
    }
    const body = {
      kind: values.kind,
      name: values.name,
      displayName: values.displayName || null,
      isAnonymous: values.kind === 'Individual' ? values.isAnonymous : false,
      contactName: values.contactName || null,
      contactRole: values.contactRole || null,
      contactEmail: values.contactEmail || null,
      tierId: values.tierId || null,
      cashAmount: cash,
      inKindDescription: values.inKindDescription || null,
      inKindItemCount: values.inKindItemCount ? parseInteger(values.inKindItemCount) : null,
      websiteUrl: values.websiteUrl || null,
    }
    if (!body.cashAmount && !body.inKindDescription) {
      setError('cashAmount', { message: 'A sponsor needs a cash amount, an in-kind contribution, or both.' })
      return
    }
    save.mutate(
      { id: sponsor?.id, body },
      {
        onSuccess: () => { toast({ tone: 'success', title: sponsor ? 'Sponsor updated.' : 'Sponsor added.' }); onClose() },
        onError: (e) => {
          const fields = isApiError(e) ? e.fieldErrors : {}
          for (const [field, message] of Object.entries(fields)) setError(field as keyof SponsorFormValues, { message })
          if (!Object.keys(fields).length) setError('name', { message: errorMessage(e) })
        },
      },
    )
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={sponsor ? 'Edit sponsor' : 'Add sponsor'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Cancel</Button>
          <Button form="sponsor-form" type="submit" loading={save.isPending}>{sponsor ? 'Save' : 'Add sponsor'}</Button>
        </>
      }
    >
      <form id="sponsor-form" className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
        <Field label="Kind">
          {(c) => (
            <Select {...c} {...register('kind')}>
              <option value="Organization">Organization</option>
              <option value="Individual">Individual</option>
            </Select>
          )}
        </Field>
        <Field label="Name" error={formState.errors.name?.message}>
          {(c) => <Input {...c} {...register('name')} aria-invalid={formState.errors.name ? true : undefined} />}
        </Field>
        {kind === 'Individual' && (
          <>
            <Field label="Display name" optional hint="How they are credited on materials.">
              {(c) => <Input {...c} {...register('displayName')} disabled={isAnonymous} />}
            </Field>
            <label className="flex items-center gap-2 text-sm">
              <Checkbox {...register('isAnonymous')} /> Credit as “Anonymous”
            </label>
          </>
        )}
        {kind === 'Organization' && (
          <div className="grid grid-cols-2 gap-3">
            <Field label="Contact name" optional>{(c) => <Input {...c} {...register('contactName')} />}</Field>
            <Field label="Contact role" optional>{(c) => <Input {...c} {...register('contactRole')} />}</Field>
          </div>
        )}
        <Field label="Contact email" optional error={formState.errors.contactEmail?.message}>
          {(c) => <Input type="email" {...c} {...register('contactEmail')} />}
        </Field>
        <Field label="Tier" optional>
          {(c) => (
            <Select {...c} {...register('tierId')}>
              <option value="">No tier</option>
              {tiers.filter((t) => t.isActive).map((t) => <option key={t.id} value={t.id}>{t.class} · {t.name}</option>)}
            </Select>
          )}
        </Field>
        <Field label="Cash amount (₱)" optional error={formState.errors.cashAmount?.message}>
          {(c) => <Input inputMode="decimal" {...c} {...register('cashAmount')} />}
        </Field>
        <div className="grid grid-cols-[1fr_auto] gap-3">
          <Field label="In-kind contribution" optional>{(c) => <Input {...c} {...register('inKindDescription')} placeholder="e.g. 400 tote bags" />}</Field>
          <Field label="Item count" optional>{(c) => <Input inputMode="numeric" className="w-28" {...c} {...register('inKindItemCount')} />}</Field>
        </div>
        {kind === 'Organization' && (
          <Field label="Website" optional error={formState.errors.websiteUrl?.message}>
            {(c) => <Input {...c} {...register('websiteUrl')} placeholder="https://" />}
          </Field>
        )}
      </form>
    </Dialog>
  )
}

function defaults(s?: Sponsor): SponsorFormValues {
  return {
    kind: s?.kind ?? 'Organization',
    name: s?.name ?? '',
    displayName: s?.displayName ?? '',
    isAnonymous: s?.isAnonymous ?? false,
    contactName: s?.contactName ?? '',
    contactRole: s?.contactRole ?? '',
    contactEmail: s?.contactEmail ?? '',
    tierId: s?.tierId ?? '',
    cashAmount: s?.cashAmount != null ? String(s.cashAmount) : '',
    inKindDescription: s?.inKindDescription ?? '',
    inKindItemCount: s?.inKindItemCount != null ? String(s.inKindItemCount) : '',
    websiteUrl: s?.websiteUrl ?? '',
  }
}
