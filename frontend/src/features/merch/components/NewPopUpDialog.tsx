import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input, Select } from '@/components/ui/Input'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { Spinner } from '@/components/ui/Spinner'
import { useDirectoryOrgs } from '@/features/directory/api'
import { fromManilaInput } from '@/features/events/format'
import { usePeopleQuery } from '@/features/organization/api'
import { useCreatePopUpMutation, usePopUpEligibleCommittees, type NewPopUpBody } from '../api'
import { newPopUpFormSchema, popUpDiscountModes, type NewPopUpFormInput, type NewPopUpFormValues, type PopUp } from '../schemas'
import { discountModeLabel } from './labels'

type TeamPick = { personId: string; name: string }

const venueOptions = [
  { value: 'org' as const, label: 'Directory organization' },
  { value: 'text' as const, label: 'Type it in' },
]

const discountModeOptions = popUpDiscountModes.map((m) => ({ value: m, label: discountModeLabel(m) }))

/** "+ Pop-up store" (canvas PopUps.dc.html). On success, hands the new pop-up to the parent for the allocation step. */
export function NewPopUpDialog({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (popUp: PopUp) => void }) {
  const orgs = useDirectoryOrgs()
  const committees = usePopUpEligibleCommittees()
  const create = useCreatePopUpMutation()
  const [team, setTeam] = useState<TeamPick[]>([])
  const [teamQuery, setTeamQuery] = useState('')

  const form = useForm<NewPopUpFormInput, unknown, NewPopUpFormValues>({
    resolver: zodResolver(newPopUpFormSchema),
    defaultValues: {
      name: '',
      venueKind: 'text',
      venueDirectoryOrgId: '',
      venueText: '',
      startsAt: '',
      endsAt: '',
      feeAmount: 0,
      discountMode: 'SameAsDefault',
      custom: { goonPct: 20, apprenticePct: 15, memberPct: 10, boardPct: 0 },
      ownerCommitteeId: '',
      teamPersonIds: [],
    },
  })
  const { register, control, handleSubmit, formState, setError, reset } = form
  const venueKind = useWatch({ control, name: 'venueKind' })
  const discountMode = useWatch({ control, name: 'discountMode' })

  const allowedCommittees = committees.committees.filter((c) => c.popUpsAllowed)
  const peopleResults = usePeopleQuery({ search: teamQuery.trim(), page: 1, pageSize: 8 }, teamQuery.trim().length >= 2)
  const staffHits = (peopleResults.data?.items ?? []).filter(
    (p) => (p.level === 'Goon' || p.level === 'Apprentice') && !team.some((t) => t.personId === p.id),
  )

  function close() {
    reset()
    setTeam([])
    setTeamQuery('')
    onClose()
  }

  function onSubmit(values: NewPopUpFormValues) {
    if (create.isPending) return
    const body: NewPopUpBody = {
      name: values.name,
      venueDirectoryOrgId: values.venueKind === 'org' ? values.venueDirectoryOrgId ?? null : null,
      venueText: values.venueKind === 'text' ? values.venueText ?? null : null,
      startsAt: fromManilaInput(values.startsAt),
      endsAt: fromManilaInput(values.endsAt),
      feeAmount: values.feeAmount,
      discountMode: values.discountMode,
      goonPct: values.discountMode === 'Custom' ? values.custom.goonPct : null,
      apprenticePct: values.discountMode === 'Custom' ? values.custom.apprenticePct : null,
      memberPct: values.discountMode === 'Custom' ? values.custom.memberPct : null,
      boardPct: values.discountMode === 'Custom' ? values.custom.boardPct : null,
      ownerCommitteeId: values.ownerCommitteeId,
      teamPersonIds: team.map((t) => t.personId),
    }
    create.mutate(body, {
      onSuccess: (popUp) => {
        close()
        onCreated(popUp)
      },
      onError: (e) => setError('name', { message: errorMessage(e) }),
    })
  }

  return (
    <Dialog
      open={open}
      onClose={close}
      title="+ Pop-up store"
      size="lg"
      description="Next you'll choose how much leftover stock to bring."
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button form="new-pop-up-form" type="submit" loading={create.isPending}>
            Create pop-up
          </Button>
        </>
      }
    >
      <form id="new-pop-up-form" className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
        {formState.errors.name?.message ? <Alert tone="red" live="alert">{formState.errors.name.message}</Alert> : null}

        <Field label="Name" error={formState.errors.name?.message}>
          {(c) => <Input {...c} {...register('name')} placeholder="HTN Pop-up · SM Megamall" />}
        </Field>

        <Field label="Venue">
          {() => <SegmentedControl options={venueOptions} value={venueKind} onChange={(v) => form.setValue('venueKind', v)} aria-label="Venue kind" fill />}
        </Field>
        {venueKind === 'org' ? (
          <Field label="Organization" error={formState.errors.venueDirectoryOrgId?.message ?? formState.errors.venueText?.message}>
            {(c) => (
              <Select {...c} {...register('venueDirectoryOrgId')}>
                <option value="">Pick an organization…</option>
                {(orgs.data ?? [])
                  .filter((o) => o.isActive)
                  .map((o) => (
                    <option key={o.id} value={o.id}>
                      {o.name}
                    </option>
                  ))}
              </Select>
            )}
          </Field>
        ) : (
          <Field label="Venue / host" error={formState.errors.venueText?.message}>
            {(c) => <Input {...c} {...register('venueText')} placeholder="University gym, City Hall grounds…" />}
          </Field>
        )}

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Field label="Starts" error={formState.errors.startsAt?.message}>
            {(c) => <Input {...c} type="datetime-local" {...register('startsAt')} />}
          </Field>
          <Field label="Ends" error={formState.errors.endsAt?.message}>
            {(c) => <Input {...c} type="datetime-local" {...register('endsAt')} />}
          </Field>
        </div>

        <Field label="Committee running this pop-up" error={formState.errors.ownerCommitteeId?.message} hint="Only committees admins have allowed to run pop-ups are listed.">
          {(c) => (
            <Select {...c} {...register('ownerCommitteeId')} disabled={committees.isPending}>
              <option value="">Pick a committee…</option>
              {allowedCommittees.map((cm) => (
                <option key={`${cm.eventId}-${cm.committeeId}`} value={cm.committeeId}>
                  {cm.name} · {cm.eventName}
                </option>
              ))}
            </Select>
          )}
        </Field>
        {committees.isPending ? <Spinner label="Loading committees" /> : null}
        {!committees.isPending && allowedCommittees.length === 0 ? (
          <Alert tone="yellow">No committee is allowed to run pop-ups yet. Turn one on below first.</Alert>
        ) : null}

        <Field label="Store team" hint="Search for a goon or apprentice to add.">
          {() => <Input value={teamQuery} onChange={(e) => setTeamQuery(e.target.value)} placeholder="Search by name…" />}
        </Field>
        {team.length > 0 ? (
          <ul className="flex flex-wrap gap-1.5">
            {team.map((t) => (
              <li key={t.personId}>
                <button
                  type="button"
                  onClick={() => setTeam((prev) => prev.filter((p) => p.personId !== t.personId))}
                  aria-label={`Remove ${t.name} from the team`}
                  className="inline-flex min-h-8 items-center gap-1 rounded-md bg-gray-100 px-2.5 py-1 text-xs font-medium text-gray-700 hover:bg-gray-200 dark:bg-white/10 dark:text-gray-200 dark:hover:bg-white/20"
                >
                  {t.name} ×
                </button>
              </li>
            ))}
          </ul>
        ) : null}
        {teamQuery.trim().length >= 2 && staffHits.length > 0 ? (
          <ul className="flex flex-col gap-1 rounded-md ring-1 ring-gray-200 dark:ring-white/10">
            {staffHits.map((p) => (
              <li key={p.id}>
                <button
                  type="button"
                  onClick={() => {
                    setTeam((prev) => [...prev, { personId: p.id, name: `${p.firstName} ${p.lastName}` }])
                    setTeamQuery('')
                  }}
                  className="flex min-h-11 w-full items-center justify-between gap-2 px-3 py-2 text-left text-sm hover:bg-gray-50 dark:hover:bg-white/5"
                >
                  <span className="text-gray-900 dark:text-gray-50">
                    {p.firstName} {p.lastName}
                  </span>
                  <Badge color="gray">{p.level}</Badge>
                </button>
              </li>
            ))}
          </ul>
        ) : null}

        <Field label="Member discounts">
          {() => <SegmentedControl options={discountModeOptions} value={discountMode} onChange={(v) => form.setValue('discountMode', v)} aria-label="Discount mode" fill />}
        </Field>
        {discountMode === 'Custom' ? (
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Field label="Goon %">{(c) => <Input {...c} type="number" min={0} max={100} {...register('custom.goonPct')} />}</Field>
            <Field label="Apprentice %">{(c) => <Input {...c} type="number" min={0} max={100} {...register('custom.apprenticePct')} />}</Field>
            <Field label="Member %">{(c) => <Input {...c} type="number" min={0} max={100} {...register('custom.memberPct')} />}</Field>
            <Field label="Board %">{(c) => <Input {...c} type="number" min={0} max={100} {...register('custom.boardPct')} />}</Field>
          </div>
        ) : null}

        <Field label="Store fee / rent (₱)" error={formState.errors.feeAmount?.message}>
          {(c) => <Input {...c} type="number" min={0} step="0.01" inputMode="decimal" {...register('feeAmount')} />}
        </Field>
      </form>
    </Dialog>
  )
}
