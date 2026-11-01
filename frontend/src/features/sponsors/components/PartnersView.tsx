import { useState } from 'react'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardBody } from '@/components/ui/Card'
import { Dialog } from '@/components/ui/Dialog'
import { Field } from '@/components/ui/Field'
import { Input, Select } from '@/components/ui/Input'
import { Skeleton } from '@/components/ui/Spinner'
import { Switch } from '@/components/ui/Switch'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/toast-context'
import {
  useCreateGroupLinkMutation,
  usePartnerActionMutation,
  usePartnersQuery,
  useSavePartnerSettingsMutation,
  useTagPartnerMutation,
} from '../api'
import { countBases, type CountBasis } from '../schemas'
import { PartnerStatusBadge } from './badges'

const basisLabels: Record<CountBasis, string> = {
  PaidOnly: 'Paid registrants only',
  AllRegistrants: 'All registrants',
  CheckedIn: 'Checked-in attendees',
}

export function PartnersView({ eventId }: { eventId: string }) {
  const query = usePartnersQuery(eventId)
  const [tagOpen, setTagOpen] = useState(false)

  if (query.isPending) return <div role="status" aria-label="Loading partners"><Skeleton className="h-64 w-full" /></div>
  if (query.isError) {
    return (
      <Alert tone="red" action={<Button size="sm" variant="secondary" onClick={() => query.refetch()}>Try again</Button>}>
        Could not load partners. {errorMessage(query.error)}
      </Alert>
    )
  }

  const page = query.data

  return (
    <div className="flex flex-col gap-6 lg:flex-row">
      <div className="flex min-w-0 flex-1 flex-col gap-4">
        <div className="flex items-center justify-between gap-3">
          <div>
            <h2 className="text-base font-semibold text-gray-900 dark:text-gray-50">Partner organizations</h2>
            <p className="text-sm text-gray-500 dark:text-gray-400">Grouped by the agency / company field on registrations.</p>
          </div>
          <Button size="sm" variant="secondary" onClick={() => setTagOpen(true)}>Tag a partner</Button>
        </div>

        <div className="overflow-x-auto">
          <Table minWidth={720}>
            <THead>
              <Tr>
                <Th>Organization</Th>
                <Th className="text-right">Registered</Th>
                <Th>Status</Th>
                <Th>Actions</Th>
              </Tr>
            </THead>
            <TBody>
              {page.partners.length === 0 && (
                <Tr><Td className="text-gray-500 dark:text-gray-400">No partners yet.</Td><Td /><Td /><Td /></Tr>
              )}
              {page.partners.map((p) => (
                <Tr key={p.id ?? p.nameKey}>
                  <Td>
                    <div className="flex flex-col">
                      <span className="font-medium">{p.displayName}</span>
                      {p.orgType && <span className="text-xs text-gray-500 dark:text-gray-400">{p.orgType}</span>}
                    </div>
                  </Td>
                  <Td numeric>{p.registered} <span className="text-xs text-gray-500 dark:text-gray-400">({p.paid} paid)</span></Td>
                  <Td><PartnerStatusBadge status={p.status} isPartner={p.isPartner} /></Td>
                  <Td><PartnerActions eventId={eventId} partner={p} settingsAllowLink={page.settings.groupLinkEnabled} /></Td>
                </Tr>
              ))}
            </TBody>
          </Table>
        </div>

        {page.unmatched.length > 0 && (
          <details className="rounded-lg ring-1 ring-gray-900/5 dark:ring-white/10">
            <summary className="cursor-pointer px-4 py-3 text-sm font-medium">Review {page.unmatched.length} unmatched names</summary>
            <ul className="border-t border-gray-100 px-4 py-2 text-sm dark:border-white/10">
              {page.unmatched.map((u) => (
                <li key={u.nameKey} className="flex items-center justify-between py-1">
                  <span>{u.sampleName}</span>
                  <span className="text-gray-500 dark:text-gray-400">{u.registered} ({u.paid} paid)</span>
                </li>
              ))}
            </ul>
          </details>
        )}
        <p className="text-xs text-gray-500 dark:text-gray-400">The Directory arrives in Phase 7 — tag partners by hand for now.</p>
      </div>

      <SettingsAside
        key={`${page.settings.threshold}-${page.settings.countBasis}-${page.settings.groupLinkEnabled}`}
        eventId={eventId}
        settings={page.settings}
        perks={page.partnerPerks}
      />
      <TagDialog key={tagOpen ? 'open' : 'closed'} eventId={eventId} open={tagOpen} onClose={() => setTagOpen(false)} />
    </div>
  )
}

function PartnerActions({ eventId, partner, settingsAllowLink }: { eventId: string; partner: { id: string | null; isTagged: boolean; isPartner: boolean; hasGroupLink: boolean; groupJoined: number }; settingsAllowLink: boolean }) {
  const action = usePartnerActionMutation(eventId)
  const createLink = useCreateGroupLinkMutation(eventId)
  const { toast } = useToast()
  if (!partner.id) {
    return partner.isPartner
      ? <span className="text-xs text-gray-500 dark:text-gray-400">Auto — tag to add assets</span>
      : <span className="text-xs text-gray-400">—</span>
  }
  const id = partner.id
  return (
    <div className="flex flex-wrap items-center gap-2">
      {partner.hasGroupLink ? (
        <span className="text-xs text-gray-600 dark:text-gray-300">Link active · {partner.groupJoined} joined</span>
      ) : settingsAllowLink && partner.isPartner ? (
        <Button
          size="sm"
          variant="secondary"
          loading={createLink.isPending}
          onClick={() => createLink.mutate(id, {
            onSuccess: (r) => { void navigator.clipboard?.writeText(r.url); toast({ tone: 'success', title: 'Group link created and copied.' }) },
            onError: (e) => toast({ tone: 'error', title: errorMessage(e) }),
          })}
        >Create group link</Button>
      ) : null}
      <Button
        size="sm"
        variant="ghost"
        onClick={() => action.mutate({ pid: id, action: 'untag', body: { reason: 'Removed by board' } }, {
          onSuccess: () => toast({ tone: 'success', title: 'Partner removed.' }),
          onError: (e) => toast({ tone: 'error', title: errorMessage(e) }),
        })}
      >Untag</Button>
    </div>
  )
}

function SettingsAside({ eventId, settings, perks }: { eventId: string; settings: { threshold: number; countBasis: CountBasis; groupLinkEnabled: boolean }; perks: string[] }) {
  const save = useSavePartnerSettingsMutation(eventId)
  const { toast } = useToast()
  const [threshold, setThreshold] = useState(settings.threshold)
  const [basis, setBasis] = useState<CountBasis>(settings.countBasis)
  const [linksOn, setLinksOn] = useState(settings.groupLinkEnabled)

  function persist(next: { threshold?: number; countBasis?: CountBasis; groupLinkEnabled?: boolean }) {
    const body = { threshold: next.threshold ?? threshold, countBasis: next.countBasis ?? basis, groupLinkEnabled: next.groupLinkEnabled ?? linksOn }
    save.mutate(body, { onError: (e) => toast({ tone: 'error', title: errorMessage(e) }) })
  }

  return (
    <Card className="lg:w-80 lg:shrink-0">
      <CardBody className="flex flex-col gap-4">
        <h2 className="text-base font-semibold text-gray-900 dark:text-gray-50">Partner rule · this event</h2>
        <div className="flex flex-col gap-1">
          <span className="text-sm text-gray-600 dark:text-gray-300">An organization becomes a partner at</span>
          <div className="flex items-center gap-3">
            <Button size="icon" variant="secondary" aria-label="Lower threshold" onClick={() => { const v = Math.max(5, threshold - 5); setThreshold(v); persist({ threshold: v }) }}>−</Button>
            <output aria-live="polite" className="w-16 text-center text-lg font-semibold tabular-nums">{threshold}</output>
            <Button size="icon" variant="secondary" aria-label="Raise threshold" onClick={() => { const v = threshold + 5; setThreshold(v); persist({ threshold: v }) }}>+</Button>
            <span className="text-sm text-gray-600 dark:text-gray-300">registrants</span>
          </div>
        </div>
        <Field label="Count">
          {(c) => (
            <Select {...c} value={basis} onChange={(e) => { const v = e.target.value as CountBasis; setBasis(v); persist({ countBasis: v }) }}>
              {countBases.map((b) => <option key={b} value={b}>{basisLabels[b]}</option>)}
            </Select>
          )}
        </Field>
        <label className="flex items-center justify-between gap-3 text-sm">
          <span>Group registration link</span>
          <Switch checked={linksOn} aria-label="Group registration link for partners" onChange={(v) => { setLinksOn(v); persist({ groupLinkEnabled: v }) }} />
        </label>
        <div>
          <span className="text-sm font-medium text-gray-700 dark:text-gray-200">Partner perks</span>
          <ul className="mt-1 list-disc pl-5 text-sm text-gray-600 dark:text-gray-300">
            {perks.map((p) => <li key={p}>{p}</li>)}
          </ul>
        </div>
      </CardBody>
    </Card>
  )
}

function TagDialog({ eventId, open, onClose }: { eventId: string; open: boolean; onClose: () => void }) {
  const tag = useTagPartnerMutation(eventId)
  const { toast } = useToast()
  const [name, setName] = useState('')
  const [orgType, setOrgType] = useState('')
  const [error, setError] = useState<string | null>(null)

  function submit() {
    if (tag.isPending) return
    if (!name.trim()) { setError('Give the organization name.'); return }
    tag.mutate({ name: name.trim(), orgType: orgType.trim() || undefined }, {
      onSuccess: () => { toast({ tone: 'success', title: 'Partner tagged.' }); onClose() },
      onError: (e) => setError(isApiError(e) ? errorMessage(e) : 'Could not tag that organization.'),
    })
  }

  return (
    <Dialog open={open} onClose={onClose} title="Tag a partner" footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button loading={tag.isPending} onClick={submit}>Tag</Button></>}>
      <div className="flex flex-col gap-4">
        {error && <Alert tone="red">{error}</Alert>}
        <Field label="Organization">{(c) => <Input {...c} value={name} onChange={(e) => { setName(e.target.value); setError(null) }} />}</Field>
        <Field label="Type" optional>{(c) => <Input {...c} value={orgType} onChange={(e) => setOrgType(e.target.value)} placeholder="University, Company…" />}</Field>
      </div>
    </Dialog>
  )
}
