import type { BadgeColor } from '@/components/ui/Badge'
import type { BuyerLevel } from '../schemas'

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
