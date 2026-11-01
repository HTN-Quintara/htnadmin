import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Textarea } from '@/components/ui/Input'
import { formatPeso } from '@/lib/format'
import { useWriteOffPopUpMutation } from '../api'
import { writeOffFormSchema, type PopUp, type WriteOffFormValues } from '../schemas'

/** CFO-only write-off for a pop-up closed with something missing (canvas PopUps.dc.html). */
export function WriteOffDialog({ popUp, onClose }: { popUp: PopUp; onClose: () => void }) {
  const writeOff = useWriteOffPopUpMutation(popUp.id)
  const form = useForm<WriteOffFormValues>({ resolver: zodResolver(writeOffFormSchema), defaultValues: { reason: '' } })
  const { register, handleSubmit, formState, setError } = form

  function onSubmit(values: WriteOffFormValues) {
    if (writeOff.isPending) return
    writeOff.mutate(values.reason, { onSuccess: onClose, onError: (e) => setError('reason', { message: errorMessage(e) }) })
  }

  return (
    <Dialog
      open
      onClose={onClose}
      title="Write off missing stock"
      description={`Posts an expense of ${formatPeso(popUp.missingValue ?? 0)} for ${popUp.name}'s missing stock to the Organization fund.`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button form="pop-up-write-off-form" type="submit" variant="destructive" loading={writeOff.isPending}>
            Write off {formatPeso(popUp.missingValue ?? 0)}
          </Button>
        </>
      }
    >
      <form id="pop-up-write-off-form" className="flex flex-col gap-3" onSubmit={handleSubmit(onSubmit)}>
        <Field label="Reason" error={formState.errors.reason?.message}>
          {(ctrl) => <Textarea {...ctrl} {...register('reason')} placeholder="Why this is being written off…" />}
        </Field>
      </form>
    </Dialog>
  )
}
