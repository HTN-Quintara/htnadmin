import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { parseAmount, parseInteger } from '@/lib/money'
import { useSaveTierMutation } from '../api'
import { tierClasses, tierFormSchema, type SponsorTier, type TierFormValues } from '../schemas'

type Props = { eventId: string; open: boolean; onClose: () => void; tier?: SponsorTier }

export function TierDialog({ eventId, open, onClose, tier }: Props) {
  const save = useSaveTierMutation(eventId)
  const { toast } = useToast()
  const { register, handleSubmit, reset, setError, formState } = useForm<TierFormValues>({
    resolver: zodResolver(tierFormSchema),
    defaultValues: defaults(tier),
  })
  useEffect(() => { if (open) reset(defaults(tier)) }, [open, tier, reset])

  function onSubmit(values: TierFormValues) {
    if (save.isPending) return
    const cash = values.cashAmount ? parseAmount(values.cashAmount) : null
    if (values.cashAmount && cash === null) { setError('cashAmount', { message: 'Enter an amount like 100000.' }); return }
    const perks = (values.perks ?? '').split('\n').map((p) => p.trim()).filter(Boolean)
    const body = {
      class: values.class,
      name: values.name,
      cashAmount: cash,
      priceNote: values.priceNote || null,
      inKindText: values.inKindText || null,
      inKindItemCount: values.inKindItemCount ? parseInteger(values.inKindItemCount) : null,
      slots: parseInteger(values.slots) ?? 1,
      compedSeats: parseInteger(values.compedSeats) ?? 0,
      perks,
    }
    if (!body.cashAmount && !body.inKindText) { setError('cashAmount', { message: 'A tier needs a cash amount, an in-kind description, or both.' }); return }
    save.mutate(
      { id: tier?.id, body },
      {
        onSuccess: () => { toast({ tone: 'success', title: tier ? 'Tier updated.' : 'Tier added.' }); onClose() },
        onError: (e) => {
          const fields = isApiError(e) ? e.fieldErrors : {}
          for (const [field, message] of Object.entries(fields)) setError(field as keyof TierFormValues, { message })
          if (!Object.keys(fields).length) setError('name', { message: errorMessage(e) })
        },
      },
    )
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={tier ? 'Edit tier' : 'Add tier'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Cancel</Button>
          <Button form="tier-form" type="submit" loading={save.isPending}>{tier ? 'Save' : 'Add tier'}</Button>
        </>
      }
    >
      <form id="tier-form" className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Class">
            {(c) => <Select {...c} {...register('class')}>{tierClasses.map((k) => <option key={k} value={k}>{k}</option>)}</Select>}
          </Field>
          <Field label="Name" error={formState.errors.name?.message}>{(c) => <Input {...c} {...register('name')} />}</Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Cash amount (₱)" optional error={formState.errors.cashAmount?.message}>{(c) => <Input inputMode="decimal" {...c} {...register('cashAmount')} />}</Field>
          <Field label="Price note" optional>{(c) => <Input {...c} {...register('priceNote')} placeholder="or equivalent" />}</Field>
        </div>
        <div className="grid grid-cols-[1fr_auto] gap-3">
          <Field label="In-kind" optional>{(c) => <Input {...c} {...register('inKindText')} placeholder="400 tote bags" />}</Field>
          <Field label="Item count" optional>{(c) => <Input inputMode="numeric" className="w-28" {...c} {...register('inKindItemCount')} />}</Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Slots">{(c) => <Input inputMode="numeric" {...c} {...register('slots')} />}</Field>
          <Field label="Comped seats">{(c) => <Input inputMode="numeric" {...c} {...register('compedSeats')} />}</Field>
        </div>
        <Field label="Perks" optional hint="One per line.">{(c) => <Textarea rows={3} {...c} {...register('perks')} />}</Field>
      </form>
    </Dialog>
  )
}

function defaults(t?: SponsorTier): TierFormValues {
  return {
    class: t?.class ?? 'Gold',
    name: t?.name ?? '',
    cashAmount: '',
    priceNote: '',
    inKindText: '',
    inKindItemCount: '',
    slots: t?.slots != null ? String(t.slots) : '1',
    compedSeats: t?.compedSeats != null ? String(t.compedSeats) : '0',
    perks: t?.perks.join('\n') ?? '',
  }
}
