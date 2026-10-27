import { useId, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { isApiError } from '@/app/api'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { FileButton } from '@/components/ui/FileButton'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { PrefixInput } from '@/components/ui/PrefixInput'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { useToast } from '@/components/ui/toast-context'
import { useCategoriesQuery, useCreateEntryMutation, useFundsQuery } from '../api'
import { entryFormSchema, type EntryFormBody, type EntryFormInput } from '../schemas'

/**
 * Adding an entry. The receipt is required and the form will not submit without one — for the CFO the row posts
 * immediately, for the finance assistant it waits; the server decides which, and this dialog never sends a status.
 */
export function LedgerEntryDialog({
  open,
  onClose,
  accounts,
  isCfo,
}: {
  open: boolean
  onClose: () => void
  accounts: { id: string; label: string; isActive: boolean }[]
  isCfo: boolean
}) {
  const formId = useId()
  const { toast } = useToast()
  const funds = useFundsQuery()
  const categories = useCategoriesQuery()
  const create = useCreateEntryMutation()
  const [receipt, setReceipt] = useState<File | null>(null)
  const [receiptError, setReceiptError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    setValue,
    control,
    reset,
    setError,
    formState: { errors },
  } = useForm<EntryFormInput, unknown, EntryFormBody>({
    resolver: zodResolver(entryFormSchema),
    defaultValues: {
      fundId: '',
      paymentAccountId: '',
      categoryId: '',
      entryDate: today(),
      direction: 'Out',
      amount: '',
      reference: '',
      description: '',
    },
  })
  const direction = useWatch({ control, name: 'direction' })

  const close = () => {
    reset()
    setReceipt(null)
    setReceiptError(null)
    onClose()
  }

  const onSubmit = handleSubmit((body) => {
    if (create.isPending) return
    if (!receipt) {
      setReceiptError('Attach the receipt. Every entry you add needs one.')
      return
    }
    create.mutate(
      { ...body, receipt },
      {
        onSuccess: (entry) => {
          toast({
            tone: 'success',
            title: entry.status === 'Posted' ? 'Entry posted.' : 'Entry added, waiting for the CFO.',
          })
          close()
        },
        onError: (e) => {
          if (isApiError(e)) {
            for (const [field, message] of Object.entries(e.fieldErrors)) {
              if (field === 'receipt') setReceiptError(message)
              else setError(field as keyof EntryFormInput, { type: 'server', message })
            }
            if (Object.keys(e.fieldErrors).length > 0) return
          }
          toast({ tone: 'error', title: 'Could not add the entry.', description: errorMessage(e) })
        },
      },
    )
  })

  return (
    <Dialog
      open={open}
      onClose={close}
      title="New entry"
      description={
        isCfo
          ? 'This posts straight away and counts towards the balances.'
          : 'This waits for the CFO to approve it before it counts.'
      }
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={create.isPending}>
            {isCfo ? 'Post entry' : 'Add entry'}
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        <Field label="Money in or out" error={errors.direction?.message}>
          {() => (
            <SegmentedControl
              options={[
                { value: 'In', label: 'Money in' },
                { value: 'Out', label: 'Money out' },
              ]}
              value={direction}
              onChange={(v) => setValue('direction', v)}
              aria-label="Money in or out"
            />
          )}
        </Field>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Fund" error={errors.fundId?.message}>
            {(p) => (
              <Select {...p} {...register('fundId')}>
                <option value="">Choose a fund</option>
                {(funds.data ?? [])
                  .filter((f) => !f.isClosed)
                  .map((f) => (
                    <option key={f.id} value={f.id}>
                      {f.name}
                    </option>
                  ))}
              </Select>
            )}
          </Field>

          <Field label="Account" error={errors.paymentAccountId?.message}>
            {(p) => (
              <Select {...p} {...register('paymentAccountId')}>
                <option value="">Choose an account</option>
                {accounts
                  .filter((a) => a.isActive)
                  .map((a) => (
                    <option key={a.id} value={a.id}>
                      {a.label}
                    </option>
                  ))}
              </Select>
            )}
          </Field>

          <Field label="Category" error={errors.categoryId?.message}>
            {(p) => (
              <Select {...p} {...register('categoryId')}>
                <option value="">Choose a category</option>
                {(categories.data ?? []).map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            )}
          </Field>

          <Field label="Date on the receipt" error={errors.entryDate?.message}>
            {(p) => <Input {...p} type="date" {...register('entryDate')} />}
          </Field>

          <Field label="Amount (₱)" error={errors.amount?.message} hint="For example 1500 or 1500.50.">
            {(p) => (
              <PrefixInput
                {...p}
                prefix="₱"
                inputMode="decimal"
                className="tabular-nums"
                {...register('amount')}
              />
            )}
          </Field>

          <Field label="Reference" optional error={errors.reference?.message} hint="OR number, or the bank reference.">
            {(p) => <Input {...p} {...register('reference')} />}
          </Field>
        </div>

        <Field label="Description" error={errors.description?.message}>
          {(p) => <Textarea {...p} rows={2} {...register('description')} />}
        </Field>

        <Field label="Receipt" error={receiptError ?? undefined} hint="A PNG, JPG or PDF, up to 5 MB.">
          {(p) => (
            <div className="flex flex-wrap items-center gap-3">
              <FileButton
                accept="image/png,image/jpeg,application/pdf"
                aria-describedby={p['aria-describedby']}
                onFile={(file) => {
                  setReceipt(file)
                  setReceiptError(null)
                }}
              >
                {receipt ? 'Choose another file' : 'Attach the receipt'}
              </FileButton>
              {receipt ? <span className="text-sm text-gray-600 dark:text-gray-400">{receipt.name}</span> : null}
            </div>
          )}
        </Field>
      </form>
    </Dialog>
  )
}

function today(): string {
  // The Manila calendar date, which is the date on the receipt.
  return new Date(Date.now() + 8 * 60 * 60 * 1000).toISOString().slice(0, 10)
}
