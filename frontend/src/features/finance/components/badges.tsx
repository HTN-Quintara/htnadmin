import { Badge } from '@/components/ui/Badge'
import type { LedgerDirection, LedgerStatus, RequestStatus } from '../schemas'

/**
 * Status colours follow the design system: green = posted/paid/balanced, yellow = waiting, red = off or rejected,
 * gray = out of play. Every badge carries its word, so colour is never the only signal.
 */
const ledgerColors: Record<LedgerStatus, 'green' | 'yellow' | 'gray'> = {
  Posted: 'green',
  Pending: 'yellow',
  Voided: 'gray',
}

export function LedgerStatusBadge({ status }: { status: LedgerStatus }) {
  return <Badge color={ledgerColors[status]}>{status}</Badge>
}

/** In and Out are words, not arrows or colours: the ledger is read by people reconciling it against a statement. */
export function DirectionBadge({ direction }: { direction: LedgerDirection }) {
  return <Badge color={direction === 'In' ? 'teal' : 'brand'}>{direction}</Badge>
}

export function BalancedBadge({ reconciled, balanced }: { reconciled: boolean; balanced: boolean }) {
  if (!reconciled) return <Badge color="yellow">Needs check</Badge>
  return balanced ? <Badge color="green">Balanced</Badge> : <Badge color="red">Off</Badge>
}

const requestColors: Record<RequestStatus, 'green' | 'yellow' | 'red' | 'gray' | 'blue'> = {
  Pending: 'yellow',
  Approved: 'blue',
  Paid: 'green',
  Rejected: 'red',
  Cancelled: 'gray',
}

export function RequestStatusBadge({ status }: { status: RequestStatus }) {
  return <Badge color={requestColors[status]}>{status}</Badge>
}
