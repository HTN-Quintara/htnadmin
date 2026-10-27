import { useId, useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Textarea } from '@/components/ui/Input'

/**
 * A reason, required. Finance never undoes anything silently — voiding an entry, reopening a month, rejecting or
 * withdrawing a request all land in the audit log with the words someone typed here.
 */
export function ReasonDialog({
  open,
  onClose,
  title,
  description,
  label,
  confirmLabel,
  pending,
  onConfirm,
  destructive = true,
}: {
  open: boolean
  onClose: () => void
  title: string
  description?: string
  label: string
  confirmLabel: string
  pending?: boolean
  onConfirm: (reason: string) => void
  destructive?: boolean
}) {
  const formId = useId()
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  const close = () => {
    setReason('')
    setError(null)
    onClose()
  }

  return (
    <Dialog
      open={open}
      onClose={close}
      title={title}
      description={description}
      size="sm"
      role="alertdialog"
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form={formId} variant={destructive ? 'destructive' : 'primary'} loading={pending}>
            {confirmLabel}
          </Button>
        </>
      }
    >
      <form
        id={formId}
        noValidate
        onSubmit={(e) => {
          e.preventDefault()
          const value = reason.trim()
          if (value.length === 0) {
            setError('A reason is required.')
            return
          }
          setError(null)
          onConfirm(value)
        }}
      >
        <Field label={label} error={error ?? undefined}>
          {(p) => <Textarea {...p} rows={3} value={reason} onChange={(e) => setReason(e.target.value)} />}
        </Field>
      </form>
    </Dialog>
  )
}
