import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { useAuth } from '@/app/auth/auth-context'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Field } from '@/components/ui/Field'
import { Input } from '@/components/ui/Input'
import { RadioCard } from '@/components/ui/RadioCard'
import { Skeleton } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import { useEmailSettingsQuery, useSaveEmailSettingsMutation, useSendTestEmailMutation } from '../api'
import {
  emailSettingsFormSchema,
  testEmailFormSchema,
  type EmailSettings,
  type EmailSettingsFormValues,
  type TestEmailFormValues,
} from '../schemas'
import { SettingsCard } from './SettingsCard'

const modeOptions = [
  { value: 'Sandbox', title: 'Sandbox', description: 'Emails land in a Mailtrap test inbox. Nobody receives them.' },
  { value: 'Live', title: 'Live', description: 'Emails go to real recipients from your verified sending domain.' },
] as const

function StatusBadge({ source }: { source: EmailSettings['source'] }) {
  if (source === 'Database') return <Badge color="green">Configured from settings</Badge>
  if (source === 'Environment') return <Badge color="blue">Using environment variables</Badge>
  return <Badge color="yellow">Not configured</Badge>
}

function toFormValues(s: EmailSettings): EmailSettingsFormValues {
  return { mode: s.mode, inboxId: s.inboxId ?? '', fromName: s.fromName, fromEmail: s.fromEmail, replyTo: s.replyTo ?? '', apiToken: '' }
}

/** "Email delivery" card: Mailtrap HTTP API settings and a test send. Settings.Manage only; the API enforces it. */
export function EmailDeliverySection() {
  const query = useEmailSettingsQuery()
  return (
    <SettingsCard
      id="email"
      title="Email delivery"
      description="Mailtrap account used to send registration, payment and ticket emails."
      action={query.isSuccess ? <StatusBadge source={query.data.source} /> : undefined}
    >
      {query.isPending ? (
        <div role="status" aria-label="Loading email delivery" className="flex flex-col gap-3">
          <Skeleton className="h-14 w-full" />
          <Skeleton className="h-11 w-full" />
          <Skeleton className="h-11 w-full" />
        </div>
      ) : query.isError ? (
        <Alert
          tone="red"
          live="alert"
          action={
            <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
              Try again
            </Button>
          }
        >
          Could not load email delivery. {errorMessage(query.error)}
        </Alert>
      ) : (
        <>
          {query.data.tokenUnreadable ? (
            <Alert tone="red" live="alert">
              The saved API token can't be decrypted (encryption keys changed). Enter the token again.
            </Alert>
          ) : null}
          {query.data.source === 'None' ? (
            <Alert tone="yellow">Emails are not being sent. Add a Mailtrap API token and sender to turn delivery on.</Alert>
          ) : null}
          <EmailSettingsForm settings={query.data} />
          <TestEmailForm />
        </>
      )}
    </SettingsCard>
  )
}

function EmailSettingsForm({ settings }: { settings: EmailSettings }) {
  const { toast } = useToast()
  const mutation = useSaveEmailSettingsMutation()
  const [replacing, setReplacing] = useState(!settings.tokenSet)
  const [clearToken, setClearToken] = useState(false)
  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isDirty },
  } = useForm<EmailSettingsFormValues>({
    resolver: zodResolver(emailSettingsFormSchema),
    defaultValues: toFormValues(settings),
  })
  const mode = useWatch({ control, name: 'mode' })
  const showSaved = settings.tokenSet && !replacing && !clearToken

  const onSubmit = handleSubmit((v) => {
    if (mutation.isPending) return
    mutation.mutate(
      {
        mode: v.mode,
        inboxId: v.mode === 'Sandbox' ? v.inboxId : v.inboxId || null,
        fromEmail: v.fromEmail,
        fromName: v.fromName,
        replyTo: v.replyTo || null,
        apiToken: clearToken ? null : v.apiToken || null,
        clearToken,
      },
      {
        onSuccess: (saved) => {
          reset(toFormValues(saved))
          setClearToken(false)
          setReplacing(!saved.tokenSet)
          toast({ tone: 'success', title: 'Email delivery saved.' })
        },
        onError: (e) => {
          const fe = isApiError(e, 400) ? e.fieldErrors : {}
          const keys = ['inboxId', 'fromEmail', 'fromName', 'replyTo', 'apiToken'] as const
          const hit = keys.filter((k) => fe[k])
          hit.forEach((k) => setError(k, { type: 'server', message: fe[k] }))
          if (hit.length === 0) toast({ tone: 'error', title: 'Could not save email delivery.', description: errorMessage(e) })
        },
      },
    )
  })

  return (
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-5">
      <fieldset className="flex flex-col gap-2">
        <legend className="mb-2 text-sm font-medium text-gray-900 dark:text-gray-50">Mode</legend>
        <div className="grid gap-2 sm:grid-cols-2">
          {modeOptions.map((o) => (
            <RadioCard key={o.value} value={o.value} title={o.title} description={o.description} checked={mode === o.value} {...register('mode')} />
          ))}
        </div>
      </fieldset>

      {showSaved ? (
        <div className="flex flex-col gap-1.5">
          <span className="text-sm font-medium text-gray-700 dark:text-gray-200">API token</span>
          <div className="flex flex-wrap items-center gap-3">
            <span className="font-mono text-sm text-gray-900 dark:text-gray-50">Saved ••••{settings.tokenLast4}</span>
            <Button variant="secondary" size="lg" onClick={() => setReplacing(true)}>
              Replace
            </Button>
            <Button variant="danger" size="lg" onClick={() => setClearToken(true)}>
              Remove
            </Button>
          </div>
        </div>
      ) : clearToken ? (
        <Alert
          tone="yellow"
          live="status"
          action={
            <Button variant="secondary" size="sm" onClick={() => setClearToken(false)}>
              Undo
            </Button>
          }
        >
          The saved API token will be removed when you save.
        </Alert>
      ) : (
        <Field
          label="API token"
          optional={settings.tokenSet}
          error={errors.apiToken?.message}
          hint={settings.tokenSet ? 'Leave blank to keep the saved token.' : 'From Mailtrap: Settings, API tokens.'}
        >
          {(p) => <Input {...p} {...register('apiToken')} type="password" autoComplete="new-password" spellCheck={false} controlSize="lg" />}
        </Field>
      )}

      {mode === 'Sandbox' ? (
        <Field label="Inbox ID" error={errors.inboxId?.message} hint="The number in the sandbox inbox URL." className="sm:max-w-xs">
          {(p) => <Input {...p} {...register('inboxId')} inputMode="numeric" autoComplete="off" controlSize="lg" />}
        </Field>
      ) : null}

      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="From name" error={errors.fromName?.message}>
          {(p) => <Input {...p} {...register('fromName')} autoComplete="off" controlSize="lg" />}
        </Field>
        <Field label="From email" error={errors.fromEmail?.message}>
          {(p) => <Input {...p} {...register('fromEmail')} type="email" autoComplete="off" controlSize="lg" />}
        </Field>
        <Field label="Reply-to" optional error={errors.replyTo?.message}>
          {(p) => <Input {...p} {...register('replyTo')} type="email" autoComplete="off" controlSize="lg" />}
        </Field>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" size="lg" loading={mutation.isPending} disabled={!isDirty && !clearToken}>
          Save email delivery
        </Button>
        {settings.updatedAt ? (
          <span className="text-xs text-gray-600 dark:text-gray-400">Last saved {new Date(settings.updatedAt).toLocaleString()}</span>
        ) : null}
      </div>
    </form>
  )
}

function TestEmailForm() {
  const { me } = useAuth()
  const mutation = useSendTestEmailMutation()
  const [sentTo, setSentTo] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<TestEmailFormValues>({
    resolver: zodResolver(testEmailFormSchema),
    defaultValues: { to: me?.email ?? '' },
  })

  const onSubmit = handleSubmit((v) => {
    if (mutation.isPending) return
    setSentTo(null)
    mutation.mutate(
      { to: v.to },
      {
        onSuccess: (res) => setSentTo(res.to),
        onError: (e) => {
          const msg = isApiError(e, 400) ? e.fieldErrors.to : undefined
          if (msg) setError('to', { type: 'server', message: msg })
        },
      },
    )
  })

  return (
    <section aria-labelledby="test-email-heading" className="flex flex-col gap-3 border-t border-gray-200 pt-5 dark:border-white/10">
      <h3 id="test-email-heading" className="text-sm font-semibold text-gray-900 dark:text-gray-50">
        Send test email
      </h3>
      <form noValidate onSubmit={onSubmit} className="flex flex-wrap items-start gap-3">
        <Field label="Recipient" error={errors.to?.message} className="min-w-60 flex-1">
          {(p) => <Input {...p} {...register('to')} type="email" autoComplete="email" controlSize="lg" />}
        </Field>
        <Button type="submit" variant="secondary" size="lg" loading={mutation.isPending} className="mt-7">
          Send test email
        </Button>
      </form>
      <div aria-live="polite">
        {sentTo ? <Alert tone="green">Test email queued for {sentTo}.</Alert> : null}
        {mutation.isError && !(isApiError(mutation.error, 400) && mutation.error.fieldErrors.to) ? (
          <Alert tone="red">
            {isApiError(mutation.error, 429)
              ? 'Too many test emails. Try again later.'
              : `Could not send the test email. ${errorMessage(mutation.error)}`}
          </Alert>
        ) : null}
      </div>
    </section>
  )
}
