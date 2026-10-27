import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { errorMessage } from '@/app/errors'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Drawer } from '@/components/ui/Drawer'
import { Field } from '@/components/ui/Field'
import { Checkbox, Input } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import { formatDate } from '@/lib/format'
import {
  useCreateContactMutation,
  useDirectoryOrg,
  useHeadChangedMutation,
  useStillCorrectMutation,
} from '../api'
import {
  contactFormSchema,
  headChangedFormSchema,
  type ContactFormValues,
  type DirectoryContact,
  type HeadChangedFormValues,
} from '../schemas'
import { orgKindLabel } from './labels'
import { HistoryLog } from './HistoryLog'
import { OrgFormDialog } from './OrgFormDialog'

type Props = { orgId: string | null; onClose: () => void }

/** Frame 18: org detail — letter addressee, cc emails, contact verification and the history log. */
export function OrgDetailAside({ orgId, onClose }: Props) {
  const query = useDirectoryOrg(orgId)
  const canManage = useCan(Policies.DirectoryManage)
  const [edit, setEdit] = useState(false)
  const [addingContact, setAddingContact] = useState(false)
  const [headChangeFor, setHeadChangeFor] = useState<DirectoryContact | null>(null)

  const open = orgId !== null
  const org = query.data?.org
  const contacts = query.data?.contacts ?? []
  const history = query.data?.history ?? []
  const primary = contacts.find((c) => c.isPrimary) ?? contacts[0]

  return (
    <Drawer open={open} onClose={onClose} label="Organization details">
      {query.isPending || !org ? (
        <div className="flex flex-col gap-3" role="status" aria-label="Loading organization">
          <Skeleton className="h-6 w-48" />
          <Skeleton className="h-40 w-full" />
        </div>
      ) : (
        <div className="flex flex-col gap-5">
          <div className="flex items-start justify-between gap-3">
            <div>
              <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-50">{org.name}</h2>
              <div className="mt-1 flex items-center gap-2">
                <Badge color="brand">{orgKindLabel(org.kind)}</Badge>
                {org.needsCheck ? <Badge color="yellow">Needs check</Badge> : <Badge color="green">Checked</Badge>}
                {!org.isActive ? <Badge color="gray">Deactivated</Badge> : null}
              </div>
            </div>
            {canManage ? <Button size="sm" variant="secondary" onClick={() => setEdit(true)}>Edit</Button> : null}
          </div>

          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
            <dt className="text-gray-500 dark:text-gray-400">Letter addressee</dt>
            <dd>
              {org.addresseeSalutation ?? <span className="text-gray-400">—</span>}
              {primary ? `, ${primary.position} ${primary.holderName}` : ''}
            </dd>
            {org.websiteUrl ? (
              <>
                <dt className="text-gray-500 dark:text-gray-400">Website</dt>
                <dd>
                  <a href={org.websiteUrl} target="_blank" rel="noreferrer" className="text-brand-600 hover:underline dark:text-brand-300">
                    {org.websiteUrl}
                  </a>
                </dd>
              </>
            ) : null}
            {primary && primary.ccEmails.length > 0 ? (
              <>
                <dt className="text-gray-500 dark:text-gray-400">CC emails</dt>
                <dd>{primary.ccEmails.join(', ')}</dd>
              </>
            ) : null}
            {org.notes ? (
              <>
                <dt className="text-gray-500 dark:text-gray-400">Notes</dt>
                <dd>{org.notes}</dd>
              </>
            ) : null}
          </dl>

          <section className="flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-50">Contacts</h3>
              {canManage ? (
                <Button size="sm" variant="secondary" onClick={() => setAddingContact(true)}>+ Contact</Button>
              ) : null}
            </div>
            {contacts.length === 0 ? (
              <p className="text-sm text-gray-500 dark:text-gray-400">No contacts yet.</p>
            ) : (
              <ul className="flex flex-col gap-3">
                {contacts.map((c) => (
                  <ContactRow key={c.id} orgId={org.id} contact={c} canManage={canManage} onHeadChange={() => setHeadChangeFor(c)} />
                ))}
              </ul>
            )}
          </section>

          <section className="flex flex-col gap-3">
            <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-50">History</h3>
            <HistoryLog history={history} />
          </section>

          <OrgFormDialog open={edit} onClose={() => setEdit(false)} org={org} />
          {canManage && <AddContactDialog open={addingContact} onClose={() => setAddingContact(false)} orgId={org.id} hasPrimary={contacts.some((c) => c.isPrimary)} />}
          {canManage && (
            <HeadChangedDialog orgId={org.id} contact={headChangeFor} onClose={() => setHeadChangeFor(null)} />
          )}
        </div>
      )}
    </Drawer>
  )
}

function ContactRow({
  orgId,
  contact,
  canManage,
  onHeadChange,
}: {
  orgId: string
  contact: DirectoryContact
  canManage: boolean
  onHeadChange: () => void
}) {
  const stillCorrect = useStillCorrectMutation()
  const { toast } = useToast()

  return (
    <li className="flex flex-col gap-1 rounded-md border border-gray-200 p-3 dark:border-white/10">
      <div className="flex items-start justify-between gap-2">
        <div>
          <p className="font-medium text-gray-900 dark:text-gray-50">{contact.position}</p>
          <p className="text-sm text-gray-700 dark:text-gray-300">{contact.holderName}</p>
          <p className="text-xs text-gray-500 dark:text-gray-400">{contact.email}{contact.phone ? ` · ${contact.phone}` : ''}</p>
        </div>
        {contact.needsCheck ? <Badge color="yellow">Needs check</Badge> : <Badge color="green">Checked</Badge>}
      </div>
      <p className="text-xs text-gray-500 dark:text-gray-400">
        {contact.verifiedAt ? `Last verified ${formatDate(contact.verifiedAt)}` : 'Never verified'}
      </p>
      {canManage ? (
        <div className="mt-1 flex gap-2">
          <Button
            size="sm"
            variant="secondary"
            loading={stillCorrect.isPending}
            onClick={() =>
              stillCorrect.mutate(
                { orgId, contactId: contact.id },
                { onSuccess: () => toast({ tone: 'success', title: 'Marked still correct.' }), onError: (e) => toast({ tone: 'error', title: errorMessage(e) }) },
              )
            }
          >
            Still correct
          </Button>
          <Button size="sm" variant="secondary" onClick={onHeadChange}>Head changed</Button>
        </div>
      ) : null}
    </li>
  )
}

function HeadChangedDialog({ orgId, contact, onClose }: { orgId: string; contact: DirectoryContact | null; onClose: () => void }) {
  const headChanged = useHeadChangedMutation()
  const { toast } = useToast()
  const open = contact !== null
  const form = useForm<HeadChangedFormValues>({ resolver: zodResolver(headChangedFormSchema), defaultValues: defaultsFor(contact) })
  const { register, handleSubmit, reset, setError, formState } = form

  useEffect(() => { if (open) reset(defaultsFor(contact)) }, [open, contact, reset])

  function onSubmit(values: HeadChangedFormValues) {
    if (!contact || headChanged.isPending) return
    headChanged.mutate(
      { orgId, contactId: contact.id, body: { position: values.position, holderName: values.holderName, email: values.email, phone: values.phone || null } },
      {
        onSuccess: () => { toast({ tone: 'success', title: 'Head changed and verified.' }); onClose() },
        onError: (e) => setError('holderName', { message: errorMessage(e) }),
      },
    )
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Head changed"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>Cancel</Button>
          <Button form="head-changed-form" type="submit" loading={headChanged.isPending}>Save</Button>
        </>
      }
    >
      <form id="head-changed-form" className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
        <Field label="Position" error={formState.errors.position?.message}>
          {(c) => <Input {...c} {...register('position')} />}
        </Field>
        <Field label="Holder name" error={formState.errors.holderName?.message}>
          {(c) => <Input {...c} {...register('holderName')} />}
        </Field>
        <Field label="Email" error={formState.errors.email?.message}>
          {(c) => <Input type="email" {...c} {...register('email')} />}
        </Field>
        <Field label="Phone" optional error={formState.errors.phone?.message}>
          {(c) => <Input {...c} {...register('phone')} />}
        </Field>
      </form>
    </Dialog>
  )
}

function defaultsFor(contact: DirectoryContact | null): HeadChangedFormValues {
  return {
    position: contact?.position ?? '',
    holderName: '',
    email: contact?.email ?? '',
    phone: contact?.phone ?? '',
  }
}

function AddContactDialog({ open, onClose, orgId, hasPrimary }: { open: boolean; onClose: () => void; orgId: string; hasPrimary: boolean }) {
  const create = useCreateContactMutation()
  const { toast } = useToast()
  const form = useForm<ContactFormValues>({
    resolver: zodResolver(contactFormSchema),
    defaultValues: { position: '', holderName: '', email: '', phone: '', ccEmails: '', isPrimary: !hasPrimary },
  })
  const { register, handleSubmit, reset, setError, formState } = form

  useEffect(() => { if (open) reset({ position: '', holderName: '', email: '', phone: '', ccEmails: '', isPrimary: !hasPrimary }) }, [open, hasPrimary, reset])

  function onSubmit(values: ContactFormValues) {
    if (create.isPending) return
    create.mutate(
      { orgId, body: { position: values.position, holderName: values.holderName, email: values.email, phone: values.phone || null, isPrimary: values.isPrimary, ccEmails: values.ccEmails || null } },
      {
        onSuccess: () => { toast({ tone: 'success', title: 'Contact added.' }); onClose() },
        onError: (e) => setError('email', { message: errorMessage(e) }),
      },
    )
  }

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Add contact"
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button form="add-contact-form" type="submit" loading={create.isPending}>Add contact</Button></>}
    >
      <form id="add-contact-form" className="flex flex-col gap-4" onSubmit={handleSubmit(onSubmit)}>
        <Field label="Position" error={formState.errors.position?.message}>{(c) => <Input {...c} {...register('position')} />}</Field>
        <Field label="Holder name" error={formState.errors.holderName?.message}>{(c) => <Input {...c} {...register('holderName')} />}</Field>
        <Field label="Email" error={formState.errors.email?.message}>{(c) => <Input type="email" {...c} {...register('email')} />}</Field>
        <Field label="Phone" optional>{(c) => <Input {...c} {...register('phone')} />}</Field>
        <Field label="CC emails" optional hint="Separate multiple addresses with a semicolon.">
          {(c) => <Input {...c} {...register('ccEmails')} />}
        </Field>
        <label className="flex items-center gap-2 text-sm">
          <Checkbox {...register('isPrimary')} /> Primary contact (used as the letter addressee)
        </label>
      </form>
    </Dialog>
  )
}
