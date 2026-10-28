import { useId, useState } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { isApiError } from '@/app/api'
import { errorMessage, messageForCode } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input, Select } from '@/components/ui/Input'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { Spinner } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { useCreatePersonMutation, useEligibleAttendeesQuery } from '../api'
import {
  accessNotes,
  createPersonSchema,
  levelLabels,
  levelSchema,
  membershipFeeChoiceLabels,
  membershipFeeChoiceSchema,
  type CreatePersonBody,
  type CreatePersonInput,
  type EligibleAttendeeHit,
} from '../schemas'

const fieldNames = ['firstName', 'middleName', 'lastName', 'email', 'position', 'level'] as const
type FieldName = (typeof fieldNames)[number]
const isFieldName = (k: string): k is FieldName => (fieldNames as readonly string[]).includes(k)

const levelOptions = levelSchema.options.map((l) => ({ value: l, label: levelLabels[l] }))
const membershipFeeOptions = membershipFeeChoiceSchema.options.map((v) => ({ value: v, label: membershipFeeChoiceLabels[v] }))

const defaults: CreatePersonInput = {
  firstName: '',
  middleName: '',
  lastName: '',
  email: '',
  position: '',
  level: 'Apprentice',
  membershipFeeChoice: 'PaidAtEvent',
}

/** Splits an attendee's full name into the dialog's separate first/last name fields (best-effort autofill). */
function splitName(name: string): { firstName: string; lastName: string } {
  const trimmed = name.trim()
  const i = trimmed.indexOf(' ')
  return i === -1 ? { firstName: trimmed, lastName: '' } : { firstName: trimmed.slice(0, i), lastName: trimmed.slice(i + 1) }
}

export function CreateAccountDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const formId = useId()
  const levelLabelId = useId()
  const levelErrorId = useId()
  const attendeeSearchId = useId()
  const { toast } = useToast()
  const mutation = useCreatePersonMutation()
  const [formError, setFormError] = useState<string | null>(null)
  const [attendeeQuery, setAttendeeQuery] = useState('')
  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    setValue,
    formState: { errors },
  } = useForm<CreatePersonInput, unknown, CreatePersonBody>({ resolver: zodResolver(createPersonSchema), defaultValues: defaults })

  const level = useWatch({ control, name: 'level' })
  const isMember = level === 'Member'
  const debouncedAttendeeQuery = useDebouncedValue(attendeeQuery, 300)
  const attendeeSearch = useEligibleAttendeesQuery(isMember ? debouncedAttendeeQuery : '')

  function pickAttendee(hit: EligibleAttendeeHit) {
    const { firstName, lastName } = splitName(hit.name)
    setValue('firstName', firstName)
    setValue('lastName', lastName)
    setValue('email', hit.email)
  }

  function close() {
    reset(defaults)
    setFormError(null)
    setAttendeeQuery('')
    mutation.reset()
    onClose()
  }

  const onSubmit = handleSubmit((body) => {
    if (mutation.isPending) return
    setFormError(null)
    mutation.mutate(body, {
      onSuccess: () => {
        toast({
          tone: 'success',
          title: body.level === 'Member' ? 'Member added to the roster.' : 'Account created. Invite email sent; it expires in 48 hours.',
        })
        close()
      },
      onError: (e) => {
        let mapped = false
        if (isApiError(e) && e.code === 'no_check_in') {
          setError('level', { type: 'server', message: messageForCode(e.code) })
          return
        }
        if (isApiError(e, 400) || isApiError(e, 409)) {
          for (const [key, message] of Object.entries(e.fieldErrors)) {
            if (isFieldName(key)) {
              setError(key, { type: 'server', message })
              mapped = true
            }
          }
        }
        if (!mapped) setFormError(errorMessage(e, { 409: 'Someone with this email is already in the organization.' }))
      },
    })
  })

  return (
    <Dialog
      open={open}
      onClose={close}
      title="Create account"
      description="There is no public sign-up. Only admins add people."
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" form={formId} loading={mutation.isPending}>
            {isMember ? 'Add to roster' : 'Create & send invite'}
          </Button>
        </>
      }
    >
      <form id={formId} noValidate onSubmit={onSubmit} className="flex flex-col gap-4">
        {formError ? (
          <Alert tone="red" live="alert">
            {formError}
          </Alert>
        ) : null}
        <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">
          <Field label="First name" error={errors.firstName?.message}>
            {(p) => <Input {...p} {...register('firstName')} autoComplete="off" />}
          </Field>
          <Field label="Middle name" optional error={errors.middleName?.message}>
            {(p) => <Input {...p} {...register('middleName')} autoComplete="off" />}
          </Field>
          <Field label="Last name" error={errors.lastName?.message}>
            {(p) => <Input {...p} {...register('lastName')} autoComplete="off" />}
          </Field>
          <Field label="Email" error={errors.email?.message}>
            {(p) => <Input {...p} {...register('email')} type="email" autoComplete="off" />}
          </Field>
          <Field label="Position" optional error={errors.position?.message} className="sm:col-span-2">
            {(p) => <Input {...p} {...register('position')} placeholder="e.g. Logistics" autoComplete="off" />}
          </Field>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <span id={levelLabelId} className="min-w-[60px] text-[0.8125rem] font-medium text-gray-700 dark:text-gray-200">
            Level
          </span>
          <Controller
            control={control}
            name="level"
            render={({ field }) => (
              <SegmentedControl
                variant="chips"
                options={levelOptions}
                value={field.value}
                onChange={field.onChange}
                aria-labelledby={levelLabelId}
                aria-describedby={errors.level ? levelErrorId : undefined}
              />
            )}
          />
          {errors.level?.message ? (
            <p id={levelErrorId} className="w-full text-xs font-medium text-red-700 dark:text-red-400">
              {errors.level.message}
            </p>
          ) : null}
        </div>
        <Alert tone="gray" live="status">
          {accessNotes[level]}
        </Alert>

        {isMember ? (
          <div className="flex flex-col gap-3.5">
            <div className="flex flex-col gap-1.5">
              <label htmlFor={attendeeSearchId} className="text-sm font-medium text-gray-700 dark:text-gray-200">
                Attendee record
              </label>
              <Input
                id={attendeeSearchId}
                value={attendeeQuery}
                onChange={(e) => setAttendeeQuery(e.target.value)}
                placeholder="Search by name or email…"
                autoComplete="off"
              />
              {attendeeSearch.isFetching && debouncedAttendeeQuery.trim().length >= 3 ? <Spinner /> : null}
              {attendeeSearch.data && attendeeSearch.data.length > 0 ? (
                <ul className="flex flex-col gap-1">
                  {attendeeSearch.data.map((hit) => (
                    <li key={hit.attendeeId}>
                      <button
                        type="button"
                        onClick={() => pickAttendee(hit)}
                        className="flex min-h-11 w-full items-center justify-between gap-2 rounded-md px-3 py-2 text-left text-sm hover:bg-gray-100 dark:hover:bg-white/10"
                      >
                        <span className="flex min-w-0 flex-col">
                          <span className="truncate text-gray-900 dark:text-gray-50">{hit.name}</span>
                          <span className="truncate text-xs text-gray-600 dark:text-gray-400">{hit.email}</span>
                        </span>
                        {hit.alreadyMember ? (
                          <Badge color="gray">Already a member</Badge>
                        ) : hit.eligible ? (
                          <Badge color="green">✓ Eligible</Badge>
                        ) : (
                          <Badge color="yellow">Hasn't checked in</Badge>
                        )}
                      </button>
                    </li>
                  ))}
                </ul>
              ) : null}
              {attendeeSearch.data && attendeeSearch.data.length === 0 && debouncedAttendeeQuery.trim().length >= 3 ? (
                <p className="text-sm text-gray-600 dark:text-gray-400">No attendee matches. Try a different name or email.</p>
              ) : null}
            </div>

            <Field label="Membership fee">
              {(p) => (
                <Select {...p} {...register('membershipFeeChoice')}>
                  {membershipFeeOptions.map((o) => (
                    <option key={o.value} value={o.value}>
                      {o.label}
                    </option>
                  ))}
                </Select>
              )}
            </Field>
          </div>
        ) : null}
      </form>
    </Dialog>
  )
}
