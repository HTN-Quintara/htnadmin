import { Badge } from '@/components/ui/Badge'
import type { SponsorStatus } from '../schemas'

export function SponsorStatusBadge({ status }: { status: SponsorStatus }) {
  return status === 'Confirmed'
    ? <Badge color="green">Confirmed</Badge>
    : <Badge color="yellow">Awaiting contract</Badge>
}

export function PartnerStatusBadge({ status, isPartner }: { status: string; isPartner: boolean }) {
  if (isPartner) return <Badge color="green">{status}</Badge>
  if (status.endsWith('to partner')) return <Badge color="yellow">{status}</Badge>
  return <Badge color="gray">{status}</Badge>
}
