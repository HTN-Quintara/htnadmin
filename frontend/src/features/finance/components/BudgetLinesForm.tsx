import { useId } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { PrefixInput } from '@/components/ui/PrefixInput'
import { buttonClass } from '@/components/ui/button-styles'
import { useToast } from '@/components/ui/toast-context'
import { useCategoriesQuery, useSaveBudgetMutation } from '../api'
import { budgetFormSchema, type BudgetFormBody, type BudgetFormInput, type EventBudget } from '../schemas'

/**
 * Editing the budget. Saving writes a new version, so the reason is part of the form rather than an afterthought —
 * it is the only record of why the numbers changed.
 */
export function BudgetLinesForm({
  budget,
  open,
  onClose,
}: {
  budget: EventBudget
  open: boolean
  onClose: () => void
}) {
  const formId = useId()
  const { toast } = useToast()
  const categories = useCategoriesQuery()
  const save = useSaveBudgetMutation(budget.eventId)

  const {
    register,
    handleSubmit,
    control,
    reset,
    setError,
    formState: { errors },
  } = useForm<BudgetFormInput, unknown, BudgetFormBody>({
    resolver: zodResolver(budgetFormSchema),
    defaultValues: {
      reason: '',
      lines:
        budget.lines.length > 0
          ? budget.lines.map((l) => ({
              categoryId: l.categoryId,
              kind: l.kind,
              label: l.label,
              amountBudgeted: String(l.amountBudgeted),
              note: l.note ?? '',
            }))
          : [{ categoryId: '', kind: 'Expense', label: '', amountBudgeted: '', note: '' }],
    },
  })
  const { fields, append, remove } = useFieldArray({ control, name: 'lines' })

  const close = () => {
    reset()
    onClose()
  }

  const onSubmit = handleSubmit((body) => {
    if (save.isPending) return
    save.mutate(body, {
      onSuccess: (saved) => {
        toast({
          tone: 'success',
          title: `Version ${saved.version?.versionNumber ?? ''} saved. The board has been notified.`,
        })
        close()
      },
      onError: (e) => {
        if (isApiError(e)) {
          for (const [field, message] of Object.entries(e.fieldErrors)) {
            // Server field names arrive as lines[0].categoryId, which is the shape react-hook-form uses too.
            setError(field as never, { type: 'server', message })
          }
          if (Object.keys(e.fieldErrors).length > 0) return
        }
        toast({ tone: 'error', title: 'Could not save the budget.', description: errorMessage(e) })
      },
    })
  })

  return (
    <Dialog
      open={open}
      onClose={close}
      title={budget.version ? `Edit the ${budget.eventName} budget` : `Start the ${budget.eventName} budget`}
      description="Saving creates a new version with your reason, and emails the board."
      size="xl"
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={save.isPending}>
            Save new version
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
        <Field label="Why is the budget changing?" error={errors.reason?.message}>
          {(p) => <Textarea {...p} rows={2} {...register('reason')} />}
        </Field>

        <div className="flex flex-col gap-3">
          {errors.lines?.message ? (
            <p className="text-sm text-red-700 dark:text-red-400">{errors.lines.message}</p>
          ) : null}
          {fields.map((field, i) => (
            <div
              key={field.id}
              className="grid gap-3 rounded-lg ring-1 ring-gray-900/5 p-3 sm:grid-cols-[1fr_1fr_8rem_8rem_auto] dark:ring-white/10"
            >
              <Field label="Category" error={errors.lines?.[i]?.categoryId?.message}>
                {(p) => (
                  <Select {...p} {...register(`lines.${i}.categoryId`)}>
                    <option value="">Choose a category</option>
                    {(categories.data ?? []).map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.name}
                      </option>
                    ))}
                  </Select>
                )}
              </Field>
              <Field label="Line" error={errors.lines?.[i]?.label?.message}>
                {(p) => <Input {...p} {...register(`lines.${i}.label`)} />}
              </Field>
              <Field label="Side" error={errors.lines?.[i]?.kind?.message}>
                {(p) => (
                  <Select {...p} {...register(`lines.${i}.kind`)}>
                    <option value="Income">Income</option>
                    <option value="Expense">Expense</option>
                  </Select>
                )}
              </Field>
              <Field label="Budget (₱)" error={errors.lines?.[i]?.amountBudgeted?.message}>
                {(p) => (
                  <PrefixInput
                    {...p}
                    prefix="₱"
                    inputMode="decimal"
                    className="tabular-nums"
                    {...register(`lines.${i}.amountBudgeted`)}
                  />
                )}
              </Field>
              <div className="flex items-end">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => remove(i)}
                  disabled={fields.length === 1}
                  aria-label={`Remove line ${i + 1}`}
                >
                  Remove
                </Button>
              </div>
            </div>
          ))}
          <button
            type="button"
            className={buttonClass('dashed')}
            onClick={() => append({ categoryId: '', kind: 'Expense', label: '', amountBudgeted: '', note: '' })}
          >
            Add a line
          </button>
        </div>
      </form>
    </Dialog>
  )
}
