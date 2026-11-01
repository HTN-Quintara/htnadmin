import { useEffect } from 'react'
import { useFieldArray, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { ColorSwatches } from '@/components/ui/ColorSwatches'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { PlusIcon } from '@/components/ui/icons'
import { useToast } from '@/components/ui/toast-context'
import { useCreateItemMutation } from '../api'
import { itemFormSchema, type ItemFormInput, type ItemFormValues } from '../schemas'

const empty: ItemFormInput = { name: '', price: 0, swatchColor: '#B71C1C', variants: [{ label: '', stockOnHand: 0 }] }

/** "+ Item" dialog (canvas frame 20 header action): name, price, swatch, then one or more variant rows. */
export function ItemFormDialog({ open, onClose, eventId }: { open: boolean; onClose: () => void; eventId: string }) {
  const create = useCreateItemMutation(eventId)
  const { toast } = useToast()

  const form = useForm<ItemFormInput, unknown, ItemFormValues>({ resolver: zodResolver(itemFormSchema), defaultValues: empty })
  const { register, control, handleSubmit, reset, setError, formState, setValue } = form
  const { fields, append, remove } = useFieldArray({ control, name: 'variants' })
  const swatchColor = useWatch({ control, name: 'swatchColor' })

  useEffect(() => {
    if (open) reset(empty)
  }, [open, reset])

  function onSubmit(values: ItemFormValues) {
    if (create.isPending) return
    create.mutate(
      { name: values.name, price: values.price, swatchColor: values.swatchColor, variants: values.variants },
      {
        onSuccess: () => {
          toast({ tone: 'success', title: `${values.name} added to the catalog.` })
          onClose()
        },
        onError: (e) => setError('name', { message: errorMessage(e) }),
      },
    )
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="+ Item"
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button form="merch-item-form" type="submit" loading={create.isPending}>
            Add item
          </Button>
        </>
      }
    >
      <form id="merch-item-form" className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
        <Field label="Name" error={formState.errors.name?.message}>
          {(c) => <Input {...c} {...register('name')} placeholder="HTN Con Tee" />}
        </Field>
        <Field label="Price" error={formState.errors.price?.message}>
          {(c) => <Input {...c} type="number" min={0} step="0.01" inputMode="decimal" {...register('price')} />}
        </Field>
        <Field label="Color" error={formState.errors.swatchColor?.message}>
          {() => <ColorSwatches aria-label="Swatch color" value={swatchColor} onChange={(hex) => setValue('swatchColor', hex, { shouldValidate: true })} />}
        </Field>

        <div className="flex flex-col gap-2">
          <div className="flex items-center justify-between">
            <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Variants &amp; starting stock</span>
            <Button type="button" variant="secondary" size="sm" onClick={() => append({ label: '', stockOnHand: 0 })}>
              <PlusIcon size={14} /> Add variant
            </Button>
          </div>
          {formState.errors.variants?.root ? <p className="text-xs font-medium text-red-700 dark:text-red-400">{formState.errors.variants.root.message}</p> : null}
          {fields.map((field, i) => (
            <div key={field.id} className="flex items-end gap-2">
              <Field label={i === 0 ? 'Label' : undefined} error={formState.errors.variants?.[i]?.label?.message} className="flex-1">
                {(c) => <Input {...c} {...register(`variants.${i}.label`)} placeholder="S, M, L, One size…" />}
              </Field>
              <Field label={i === 0 ? 'Starting stock' : undefined} error={formState.errors.variants?.[i]?.stockOnHand?.message} className="w-32">
                {(c) => <Input {...c} type="number" min={0} inputMode="numeric" {...register(`variants.${i}.stockOnHand`)} />}
              </Field>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                aria-label={`Remove variant ${i + 1}`}
                disabled={fields.length <= 1}
                onClick={() => remove(i)}
              >
                ×
              </Button>
            </div>
          ))}
        </div>
      </form>
    </Dialog>
  )
}
