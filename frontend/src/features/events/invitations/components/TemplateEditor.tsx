import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import {
  useInvitationTemplate,
  usePreviewInvitationMutation,
  useUpdateInvitationTemplateMutation,
} from '../api'
import { mergeTokens, templateFormSchema, type InvitationListItem, type TemplateFormValues } from '../schemas'

type Props = {
  eventId: string
  /** Any picked orgs, so "Preview" can merge against a real addressee. */
  invitations: InvitationListItem[]
}

/** Step 3, "Prepare letter": the event's invitation letter subject/body, with the literal merge tokens as a reference. */
export function TemplateEditor({ eventId, invitations }: Props) {
  const query = useInvitationTemplate(eventId)
  const save = useUpdateInvitationTemplateMutation(eventId)
  const { toast } = useToast()
  const [previewFor, setPreviewFor] = useState('')
  const [previewOpen, setPreviewOpen] = useState(false)
  const preview = usePreviewInvitationMutation(eventId)

  const { register, handleSubmit, reset, formState } = useForm<TemplateFormValues>({
    resolver: zodResolver(templateFormSchema),
    defaultValues: { subject: '', body: '' },
  })

  useEffect(() => {
    if (query.data) reset({ subject: query.data.subject, body: query.data.body })
  }, [query.data, reset])

  // Derived, not synced via an effect: falls back to the first invitation when nothing (valid) is picked yet.
  const effectivePreviewFor = invitations.some((i) => i.id === previewFor) ? previewFor : (invitations[0]?.id ?? '')

  function onSubmit(values: TemplateFormValues) {
    if (save.isPending) return
    save.mutate(values, {
      onSuccess: () => toast({ tone: 'success', title: 'Letter template saved.' }),
      onError: (e) => toast({ tone: 'error', title: 'Could not save the template.', description: errorMessage(e) }),
    })
  }

  function onPreview() {
    if (!effectivePreviewFor || preview.isPending) return
    preview.mutate(effectivePreviewFor, {
      onSuccess: () => setPreviewOpen(true),
      onError: (e) => toast({ tone: 'error', title: 'Could not preview the letter.', description: errorMessage(e) }),
    })
  }

  return (
    <Card className="flex flex-col gap-4 p-5">
      <div>
        <h2 className="text-sm font-semibold text-gray-900 dark:text-gray-50">Letter template</h2>
        <p className="text-xs text-gray-600 dark:text-gray-400">Letters can only go to contacts verified in the last 90 days.</p>
      </div>

      {query.isPending ? (
        <div role="status" aria-label="Loading template">
          <Skeleton className="h-40 w-full" />
        </div>
      ) : query.isError ? (
        <Alert tone="red" action={<Button size="sm" variant="secondary" onClick={() => query.refetch()}>Try again</Button>}>
          Could not load the template. {errorMessage(query.error)}
        </Alert>
      ) : (
        <form className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
          <Field label="Subject" error={formState.errors.subject?.message}>
            {(c) => <Input {...c} {...register('subject')} />}
          </Field>
          <Field label="Body" error={formState.errors.body?.message} hint="Blank lines mark paragraph breaks.">
            {(c) => <Textarea {...c} rows={10} {...register('body')} />}
          </Field>
          <div className="flex flex-wrap gap-1.5" aria-label="Merge tokens">
            {mergeTokens.map((t) => (
              <code key={t} className="rounded bg-gray-100 px-1.5 py-0.5 text-xs text-gray-700 dark:bg-white/10 dark:text-gray-300">
                {t}
              </code>
            ))}
          </div>

          {invitations.length > 0 ? (
            <div className="flex flex-wrap items-end gap-2">
              <Field label="Preview against" className="min-w-40 flex-1">
                {(c) => (
                  <Select {...c} value={effectivePreviewFor} onChange={(e) => setPreviewFor(e.target.value)}>
                    {invitations.map((i) => (
                      <option key={i.id} value={i.id}>
                        {i.orgName}
                      </option>
                    ))}
                  </Select>
                )}
              </Field>
              <Button type="button" variant="secondary" onClick={onPreview} loading={preview.isPending}>
                Preview
              </Button>
            </div>
          ) : (
            <p className="text-xs text-gray-500 dark:text-gray-400">Pick an organization to preview the merged letter.</p>
          )}

          <div className="flex justify-end">
            <Button type="submit" loading={save.isPending}>
              Save template
            </Button>
          </div>
        </form>
      )}

      <Dialog open={previewOpen} onClose={() => setPreviewOpen(false)} title="Letter preview" size="md">
        {preview.data ? (
          <div className="flex flex-col gap-3">
            <div>
              <p className="text-xs font-semibold text-gray-500 dark:text-gray-400">Subject</p>
              <p className="text-sm font-medium text-gray-900 dark:text-gray-50">{preview.data.subject}</p>
            </div>
            <div>
              <p className="text-xs font-semibold text-gray-500 dark:text-gray-400">Body</p>
              <p className="whitespace-pre-wrap text-sm text-gray-700 dark:text-gray-300">{preview.data.body}</p>
            </div>
          </div>
        ) : null}
      </Dialog>
    </Card>
  )
}
