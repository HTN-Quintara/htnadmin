import { useState } from 'react'
import { useParams } from 'react-router'
import { Button } from '@/components/ui/Button'
import { PageHeader } from '@/components/ui/PageHeader'
import { useInvitationTemplate, useInvitations } from './api'
import { InvitationsTable } from './components/InvitationsTable'
import { PickOrgsDialog } from './components/PickOrgsDialog'
import { StepIndicator, type Step } from './components/StepIndicator'
import { TemplateEditor } from './components/TemplateEditor'

/** Frame 19: invitations per event — pick, verify, prepare the letter, send and track replies. Board-only. */
export function InvitationsPage() {
  const { eventId = '' } = useParams()
  const [picking, setPicking] = useState(false)
  const all = useInvitations(eventId)
  const template = useInvitationTemplate(eventId)
  const invitations = all.data ?? []

  const steps: Step[] = [
    { label: 'Pick list', done: invitations.length > 0 },
    { label: 'Verify contacts', done: invitations.length > 0 && invitations.every((i) => i.status !== 'NotSent' || i.primaryContactVerified) },
    { label: 'Prepare letter', done: !!template.data && template.data.subject.trim() !== '' && template.data.body.trim() !== '' },
    { label: 'Send', done: invitations.some((i) => i.status !== 'NotSent') },
    { label: 'Track replies', done: false },
  ]

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        title="Invitations"
        description="Pick organizations for this event, verify their contact, prepare the letter, then send and track replies."
      />
      <StepIndicator steps={steps} />

      <div className="flex justify-end">
        <Button size="sm" onClick={() => setPicking(true)}>+ Organizations</Button>
      </div>

      <div className="grid grid-cols-1 gap-5 lg:grid-cols-[1fr_22rem]">
        <InvitationsTable eventId={eventId} />
        <TemplateEditor eventId={eventId} invitations={invitations} />
      </div>

      <PickOrgsDialog
        eventId={eventId}
        open={picking}
        onClose={() => setPicking(false)}
        alreadyInvitedOrgIds={invitations.map((i) => i.directoryOrgId)}
      />
    </div>
  )
}
