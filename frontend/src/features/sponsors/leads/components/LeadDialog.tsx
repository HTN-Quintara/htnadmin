import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { parseAmount } from '@/lib/money'
import { useSaveLeadMutation } from '../api'
import { leadFormSchema, type LeadFormValues, type LeadKind } from '../schemas'

export function LeadDialog({ eventId, kind, open, onClose }: { eventId: string; kind: LeadKind; open: boolean; onClose: () => void }) {
  const save = useSaveLeadMutation(eventId)
  const { toast } = useToast()
  const { register, handleSubmit, setError, formState } = useForm<LeadFormValues>({
    resolver: zodResolver(leadFormSchema),
    defaultValues: { kind, subject: '', contactName: '', contactRole: '', contactEmail: '', askAmount: '', askNote: '', nextStep: '', nextStepDue: '' },
  })

  function onSubmit(values: LeadFormValues) {
    if (save.isPending) return
    const amount = values.askAmount ? parseAmount(values.askAmount) : null
    if (values.askAmount && amount === null) { setError('askAmount', { message: 'Enter an amount like 100000.' }); return }
    const body = {
      kind,
      subject: values.subject,
      contactName: values.contactName || null,
      contactRole: values.contactRole || null,
      contactEmail: values.contactEmail || null,
      askAmount: kind === 'Sponsor' ? amount : null,
      askNote: values.askNote || null,
      nextStep: values.nextStep || null,
      nextStepDue: values.nextStepDue || null,
    }
    save.mutate({ body }, {
      onSuccess: () => { toast({ tone: 'success', title: 'Lead added.' }); onClose() },
      onError: (e) => {
        const fields = isApiError(e) ? e.fieldErrors : {}
        for (const [f, m] of Object.entries(fields)) setError(f as keyof LeadFormValues, { message: m })
        if (!Object.keys(fields).length) setError('subject', { message: errorMessage(e) })
      },
    })
  }

  return (
    <Dialog open={open} onClose={onClose} title={`Add ${kind === 'Sponsor' ? 'sponsor' : 'speaker'} lead`}
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button form="lead-form" type="submit" loading={save.isPending}>Add lead</Button></>}>
      <form id="lead-form" className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
        <Field label={kind === 'Sponsor' ? 'Organization' : 'Name'} error={formState.errors.subject?.message}>
          {(c) => <Input {...c} {...register('subject')} />}
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Contact name" optional>{(c) => <Input {...c} {...register('contactName')} />}</Field>
          <Field label="Contact role" optional>{(c) => <Input {...c} {...register('contactRole')} />}</Field>
        </div>
        <Field label="Contact email" optional hint={kind === 'Speaker' ? 'Needed to send the confirmation link.' : undefined} error={formState.errors.contactEmail?.message}>
          {(c) => <Input type="email" {...c} {...register('contactEmail')} />}
        </Field>
        {kind === 'Sponsor' && (
          <div className="grid grid-cols-2 gap-3">
            <Field label="Ask amount (₱)" optional error={formState.errors.askAmount?.message}>{(c) => <Input inputMode="decimal" {...c} {...register('askAmount')} />}</Field>
            <Field label="Ask note" optional>{(c) => <Input {...c} {...register('askNote')} placeholder="Gold" />}</Field>
          </div>
        )}
        {kind === 'Speaker' && (
          <Field label="Ask note" optional>{(c) => <Input {...c} {...register('askNote')} placeholder="Keynote topic" />}</Field>
        )}
        <div className="grid grid-cols-2 gap-3">
          <Field label="Next step" optional>{(c) => <Input {...c} {...register('nextStep')} />}</Field>
          <Field label="Due" optional>{(c) => <Input type="date" {...c} {...register('nextStepDue')} />}</Field>
        </div>
      </form>
    </Dialog>
  )
}
