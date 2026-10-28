import type { BadgeColor } from '@/components/ui/Badge'
import type { BuyerLevel, PopUpDiscountMode, PopUpStatus } from '../schemas'

/** Canvas colors: Goon amber-red, Apprentice teal, Member gray, Board blue, Attendee plain gray. */
const levelColors: Record<BuyerLevel, BadgeColor> = {
  Goon: 'red',
  Apprentice: 'teal',
  Member: 'gray',
  Board: 'blue',
  Attendee: 'gray',
}

export function levelBadgeColor(level: BuyerLevel): BadgeColor {
  return levelColors[level]
}

const methodLabels: Record<string, string> = {
  Cash: 'Cash',
  GCash: 'GCash',
  Bank: 'Bank transfer',
}

export function paymentMethodLabel(method: string): string {
  return methodLabels[method] ?? method
}

/** Canvas copy for a pop-up's status chip (PopUps.dc.html). "Closed · N missing" needs the live count, added by the caller. */
const popUpStatusLabels: Record<PopUpStatus, string> = {
  Planned: 'Planned',
  Open: 'Open now',
  NeedsCount: 'Needs count',
  ClosedBalanced: 'Closed · balanced',
  ClosedMissing: 'Closed · missing',
}

/** Planned gray, Open green, NeedsCount amber, ClosedBalanced blue, ClosedMissing red. */
const popUpStatusColors: Record<PopUpStatus, BadgeColor> = {
  Planned: 'gray',
  Open: 'green',
  NeedsCount: 'yellow',
  ClosedBalanced: 'blue',
  ClosedMissing: 'red',
}

export function popUpStatusLabel(status: PopUpStatus): string {
  return popUpStatusLabels[status]
}

export function popUpStatusBadgeColor(status: PopUpStatus): BadgeColor {
  return popUpStatusColors[status]
}

const discountModeLabels: Record<PopUpDiscountMode, string> = {
  SameAsDefault: 'Same as default',
  None: 'No discounts',
  Custom: 'Custom',
}

export function discountModeLabel(mode: PopUpDiscountMode): string {
  return discountModeLabels[mode]
}
