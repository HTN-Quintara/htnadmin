import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { useCountPopUpMutation } from '../api'
import { countFormSchema, type CountFormInput, type CountFormValues, type PopUp } from '../schemas'

/** "Close & count" (canvas PopUps.dc.html): returned qty per variant (capped at brought − sold) + cash counted. */
export function CloseAndCountDialog({ popUp, onClose }: { popUp: PopUp; onClose: () => void }) {
  const count = useCountPopUpMutation(popUp.id)
  const form = useForm<CountFormInput, unknown, CountFormValues>({
    resolver: zodResolver(countFormSchema),
    defaultValues: {
      lines: popUp.allocations.map((a) => ({
        variantId: a.variantId,
        itemName: a.itemName,
        variantLabel: a.variantLabel,
        max: Math.max(0, a.qtyBrought - a.qtySold),
        qtyReturned: 0,
      })),
      cashCounted: 0,
    },
  })
  const { register, handleSubmit, formState, setError } = form

  function onSubmit(values: CountFormValues) {
    if (count.isPending) return
    const over = values.lines.find((l) => l.qtyReturned > l.max)
    if (over) {
      setError('cashCounted', { message: `Returned can't exceed ${over.max} for "${over.itemName} · ${over.variantLabel}".` })
      return
    }
    count.mutate(
      { returned: values.lines.map((l) => ({ variantId: l.variantId, qtyReturned: l.qtyReturned })), cashCounted: values.cashCounted },
      { onSuccess: onClose, onError: (e) => setError('cashCounted', { message: errorMessage(e) }) },
    )
  }

  return (
    <Dialog
      open
      onClose={onClose}
      title="Close store & count"
      size="lg"
      description={`Count what came back from ${popUp.name} and the cash in the drawer.`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button form="pop-up-count-form" type="submit" loading={count.isPending}>
            Record count
          </Button>
        </>
      }
    >
      <form id="pop-up-count-form" className="flex flex-col gap-3" onSubmit={handleSubmit(onSubmit)}>
        {popUp.allocations.length === 0 ? (
          <p className="text-sm text-gray-600 dark:text-gray-400">No stock was allocated to this pop-up.</p>
        ) : (
          popUp.allocations.map((a, i) => (
            <Field
              key={a.variantId}
              label={`${a.itemName} (${a.variantLabel}) — returned`}
              hint={`Brought ${a.qtyBrought}, sold ${a.qtySold}. Up to ${Math.max(0, a.qtyBrought - a.qtySold)} can be returned.`}
              error={formState.errors.lines?.[i]?.qtyReturned?.message}
            >
              {(ctrl) => (
                <Input {...ctrl} type="number" min={0} max={Math.max(0, a.qtyBrought - a.qtySold)} inputMode="numeric" {...register(`lines.${i}.qtyReturned`)} />
              )}
            </Field>
          ))
        )}
        <Field label="Cash counted" error={formState.errors.cashCounted?.message}>
          {(ctrl) => <Input {...ctrl} type="number" min={0} step="0.01" inputMode="decimal" {...register('cashCounted')} />}
        </Field>
      </form>
    </Dialog>
  )
}
