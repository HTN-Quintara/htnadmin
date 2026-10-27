import { useState } from 'react'
import { Link } from 'react-router'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Badge, type BadgeColor } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { FilterChip } from '@/components/ui/FilterChip'
import { Checkbox, Select } from '@/components/ui/Input'
import { BuildingIcon } from '@/components/ui/icons'
import { Skeleton } from '@/components/ui/Spinner'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/toast-context'
import { formatDate } from '@/lib/format'
import {
  useAdvanceInvitationStatusMutation,
  useInvitations,
  useSendInvitationMutation,
} from '../api'
import { invitationTransitions, type InvitationListItem, type InvitationStatus } from '../schemas'

type Tab = 'All' | 'NotSent' | 'Sent' | 'Replied' | 'Bounced'
const tabs: { value: Tab; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'NotSent', label: 'Not sent' },
  { value: 'Sent', label: 'Sent' },
  { value: 'Replied', label: 'Replied' },
  { value: 'Bounced', label: 'Bounced' },
]

const statusColor: Record<InvitationStatus, BadgeColor> = {
  NotSent: 'gray',
  Sent: 'blue',
  Opened: 'teal',
  Replied: 'brand',
  Confirmed: 'green',
  Declined: 'red',
  Bounced: 'yellow',
}

const statusLabel: Record<InvitationStatus, string> = {
  NotSent: 'Not sent',
  Sent: 'Sent',
  Opened: 'Opened',
  Replied: 'Replied',
  Confirmed: 'Confirmed',
  Declined: 'Declined',
  Bounced: 'Bounced',
}

type Props = { eventId: string }

/** Step 4/5, "Send" and "Track replies": the pick list with status tabs, verification gate and manual advance. */
export function InvitationsTable({ eventId }: Props) {
  const [tab, setTab] = useState<Tab>('All')
  const [selected, setSelected] = useState<string[]>([])
  const query = useInvitations(eventId, tab === 'All' ? undefined : (tab as InvitationStatus))
  const send = useSendInvitationMutation(eventId)
  const advance = useAdvanceInvitationStatusMutation(eventId)
  const { toast } = useToast()

  if (query.isPending) {
    return (
      <div role="status" aria-label="Loading invitations" className="flex flex-col gap-3">
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    )
  }

  if (query.isError) {
    return (
      <Alert tone="red" action={<Button size="sm" variant="secondary" onClick={() => query.refetch()}>Try again</Button>}>
        Could not load invitations. {errorMessage(query.error)}
      </Alert>
    )
  }

  const rows = query.data ?? []
  const sendableIds = new Set(rows.filter((r) => r.status === 'NotSent' && r.primaryContactVerified).map((r) => r.id))

  function toggle(id: string) {
    setSelected((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]))
  }

  function onSend(invitation: InvitationListItem) {
    if (send.isPending) return
    send.mutate(invitation.id, {
      onSuccess: () => {
        setSelected((prev) => prev.filter((x) => x !== invitation.id))
        toast({ tone: 'success', title: `Letter sent to ${invitation.orgName}.` })
      },
      onError: (e) => toast({ tone: 'error', title: 'Could not send the letter.', description: errorMessage(e) }),
    })
  }

  function onSendSelected() {
    const ids = selected.filter((id) => sendableIds.has(id))
    for (const id of ids) {
      const invitation = rows.find((r) => r.id === id)
      if (invitation) onSend(invitation)
    }
  }

  function onAdvance(invitation: InvitationListItem, status: InvitationStatus) {
    if (advance.isPending) return
    advance.mutate(
      { invitationId: invitation.id, status },
      {
        onSuccess: () => toast({ tone: 'success', title: `${invitation.orgName} marked ${statusLabel[status]}.` }),
        onError: (e) => toast({ tone: 'error', title: 'Could not update the status.', description: errorMessage(e) }),
      },
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap gap-2" role="tablist" aria-label="Invitation status">
          {tabs.map((t) => (
            <FilterChip key={t.value} selected={tab === t.value} onClick={() => setTab(t.value)}>
              {t.label}
            </FilterChip>
          ))}
        </div>
        {selected.length > 0 ? (
          <Button size="sm" onClick={onSendSelected} loading={send.isPending}>
            Send {selected.length} selected
          </Button>
        ) : null}
      </div>

      {rows.length === 0 ? (
        <EmptyState
          icon={<BuildingIcon />}
          title={tab === 'All' ? 'No invitations yet' : 'No invitations match this filter'}
          description={tab === 'All' ? 'Pick organizations from the directory to start inviting them.' : 'Try another tab.'}
        />
      ) : (
        <Table minWidth={760}>
          <THead>
            <Tr>
              <Th>
                <span className="sr-only">Select</span>
              </Th>
              <Th>Organization</Th>
              <Th>Contact</Th>
              <Th>Status</Th>
              <Th>Sent</Th>
              <Th>
                <span className="sr-only">Actions</span>
              </Th>
            </Tr>
          </THead>
          <TBody>
            {rows.map((inv) => {
              const nextStatuses = invitationTransitions[inv.status]
              const canSend = sendableIds.has(inv.id)
              return (
                <Tr key={inv.id}>
                  <Td>
                    <Checkbox
                      checked={selected.includes(inv.id)}
                      disabled={!canSend}
                      onChange={() => toggle(inv.id)}
                      aria-label={`Select ${inv.orgName} to send`}
                    />
                  </Td>
                  <Td>
                    <div className="flex flex-col">
                      <span className="font-medium text-gray-900 dark:text-gray-50">{inv.orgName}</span>
                      <span className="text-xs text-gray-500 dark:text-gray-400">{inv.orgKind}</span>
                    </div>
                  </Td>
                  <Td>
                    {inv.status !== 'NotSent' ? (
                      <Badge color="green">Sent to verified contact</Badge>
                    ) : inv.primaryContactVerified ? (
                      <Badge color="green">Verified</Badge>
                    ) : (
                      <Link
                        to="/directory"
                        className="inline-flex min-h-8 items-center text-sm font-medium text-brand-600 hover:underline dark:text-brand-300"
                      >
                        Verify first →
                      </Link>
                    )}
                  </Td>
                  <Td>
                    <Badge color={statusColor[inv.status]}>{statusLabel[inv.status]}</Badge>
                  </Td>
                  <Td>{inv.sentAt ? formatDate(inv.sentAt) : <span className="text-gray-400">—</span>}</Td>
                  <Td>
                    <div className="flex flex-wrap items-center justify-end gap-2">
                      {inv.status === 'NotSent' ? (
                        <Button size="sm" disabled={!canSend} loading={send.isPending} onClick={() => onSend(inv)}>
                          Send
                        </Button>
                      ) : nextStatuses.length > 0 ? (
                        <label className="flex items-center gap-1.5">
                          <span className="sr-only">Move {inv.orgName} to</span>
                          <Select
                            controlSize="md"
                            value=""
                            disabled={advance.isPending}
                            onChange={(e) => {
                              const next = e.target.value as InvitationStatus
                              if (next) onAdvance(inv, next)
                            }}
                            className="h-9 w-40"
                          >
                            <option value="">Move to…</option>
                            {nextStatuses.map((s) => (
                              <option key={s} value={s}>
                                {statusLabel[s]}
                              </option>
                            ))}
                          </Select>
                        </label>
                      ) : null}
                    </div>
                  </Td>
                </Tr>
              )
            })}
          </TBody>
        </Table>
      )}
    </div>
  )
}
