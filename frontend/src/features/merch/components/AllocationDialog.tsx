import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { PageSpinner } from '@/components/ui/Spinner'
import { useAllocatePopUpMutation, useLeftoverInventory } from '../api'
import { allocationFormSchema, type AllocationFormInput, type AllocationFormValues, type PopUp } from '../schemas'

/**
 * "Stock to bring" (canvas: the mock names this step but doesn't show its UI — this is the design chosen for it).
 * Allocating is additive on the server (each call reserves new lines; a variant already allocated to this pop-up
 * can't be re-allocated), so once some stock is already brought, this only offers variants not yet allocated —
 * "editing" an existing line isn't a real server operation this phase.
 */
export function AllocationDialog({ popUp, onClose }: { popUp: PopUp; onClose: () => void }) {
  const leftover = useLeftoverInventory()
  const allocate = useAllocatePopUpMutation(popUp.id)

  const allocatedIds = new Set(popUp.allocations.map((a) => a.variantId))
  const choices = (leftover.data ?? []).filter((v) => !allocatedIds.has(v.variantId) && v.available > 0)

  return (
    <Dialog
      open
      onClose={onClose}
      title="Stock to bring"
      size="lg"
      description={`Leftover stock reserved for ${popUp.name}. Already-allocated items can't be changed here.`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button form="pop-up-allocation-form" type="submit" loading={allocate.isPending}>
            Save
          </Button>
        </>
      }
    >
      {popUp.allocations.length > 0 ? (
        <div className="mb-4 flex flex-col gap-1.5 rounded-md bg-gray-50 p-3 text-sm dark:bg-white/5">
          <span className="font-medium text-gray-900 dark:text-gray-50">Already allocated</span>
          <ul className="flex flex-col gap-0.5 text-gray-600 dark:text-gray-400">
            {popUp.allocations.map((a) => (
              <li key={a.variantId}>
                {a.itemName} ({a.variantLabel}): {a.qtyBrought}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {leftover.isPending ? <PageSpinner label="Loading leftover stock" /> : null}
      {leftover.isError ? (
        <Alert tone="red" live="alert" action={<Button size="sm" variant="secondary" onClick={() => void leftover.refetch()}>Try again</Button>}>
          Could not load leftover stock. {errorMessage(leftover.error)}
        </Alert>
      ) : null}
      {leftover.data && choices.length === 0 ? (
        <Alert tone="gray">No more leftover stock is available to allocate right now.</Alert>
      ) : null}
      {leftover.data && choices.length > 0 ? <AllocationForm popUp={popUp} choices={choices} onDone={onClose} /> : null}
    </Dialog>
  )
}

function AllocationForm({
  popUp,
  choices,
  onDone,
}: {
  popUp: PopUp
  choices: Array<{ variantId: string; itemName: string; variantLabel: string; available: number }>
  onDone: () => void
}) {
  const allocate = useAllocatePopUpMutation(popUp.id)
  const form = useForm<AllocationFormInput, unknown, AllocationFormValues>({
    resolver: zodResolver(allocationFormSchema),
    defaultValues: { lines: choices.map((c) => ({ variantId: c.variantId, itemName: c.itemName, variantLabel: c.variantLabel, available: c.available, qty: 0 })) },
  })
  const { register, handleSubmit, formState, setError } = form

  function onSubmit(values: AllocationFormValues) {
    if (allocate.isPending) return
    const lines = values.lines.filter((l) => l.qty > 0)
    if (lines.length === 0) {
      setError('lines', { message: 'Enter at least one quantity.' })
      return
    }
    const over = values.lines.find((l) => l.qty > l.available)
    if (over) {
      setError('lines', { message: `Only ${over.available} of "${over.itemName} · ${over.variantLabel}" are available.` })
      return
    }
    allocate.mutate(
      lines.map((l) => ({ variantId: l.variantId, qty: l.qty })),
      { onSuccess: onDone, onError: (e) => setError('lines', { message: errorMessage(e) }) },
    )
  }

  return (
    <form id="pop-up-allocation-form" className="flex flex-col gap-3" onSubmit={handleSubmit(onSubmit)}>
      {formState.errors.lines?.message ? <Alert tone="red" live="alert">{formState.errors.lines.message}</Alert> : null}
      {choices.map((c, i) => (
        <Field key={c.variantId} label={`${c.itemName} (${c.variantLabel})`} hint={`${c.available} available`}>
          {(ctrl) => <Input {...ctrl} type="number" min={0} max={c.available} inputMode="numeric" {...register(`lines.${i}.qty`)} />}
        </Field>
      ))}
    </form>
  )
}
