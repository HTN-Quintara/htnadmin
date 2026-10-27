import type { BadgeColor } from '@/components/ui/Badge'
import type { HireAgain, SupplierCategory, SupplierDocumentKind, SupplierHireStatus, SupplierRatingTag } from '../schemas'

const categoryLabels: Record<SupplierCategory, string> = {
  Venue: 'Venue',
  Catering: 'Catering',
  Printing: 'Printing',
  TechAV: 'Tech & AV',
  Merch: 'Merch',
  Transport: 'Transport',
}

export function categoryLabel(c: SupplierCategory): string {
  return categoryLabels[c]
}

const statusLabels: Record<SupplierHireStatus, string> = {
  Quotation: 'Quotation',
  Booked: 'Booked',
  DepositPaid: 'Deposit paid',
  FullyPaid: 'Fully paid',
}

export function hireStatusLabel(s: SupplierHireStatus): string {
  return statusLabels[s]
}

const statusColors: Record<SupplierHireStatus, BadgeColor> = {
  Quotation: 'gray',
  Booked: 'blue',
  DepositPaid: 'yellow',
  FullyPaid: 'green',
}

export function hireStatusColor(s: SupplierHireStatus): BadgeColor {
  return statusColors[s]
}

/** The one allowed next step from a status, or null once FullyPaid (forward-only, one step at a time). */
const nextStatus: Record<SupplierHireStatus, SupplierHireStatus | null> = {
  Quotation: 'Booked',
  Booked: 'DepositPaid',
  DepositPaid: 'FullyPaid',
  FullyPaid: null,
}

export function nextHireStatus(s: SupplierHireStatus): SupplierHireStatus | null {
  return nextStatus[s]
}

const documentKindLabels: Record<SupplierDocumentKind, string> = {
  Bir2303: 'BIR 2303',
  BusinessPermit: 'Business permit',
  Other: 'Other',
}

export function documentKindLabel(k: SupplierDocumentKind): string {
  return documentKindLabels[k]
}

const hireAgainLabels: Record<HireAgain, string> = {
  Yes: 'Yes',
  Maybe: 'Maybe',
  No: 'No',
}

export function hireAgainLabel(h: HireAgain): string {
  return hireAgainLabels[h]
}

const tagLabels: Record<SupplierRatingTag, string> = {
  OnTime: 'On time',
  GoodQuality: 'Good quality',
  EasyToReach: 'Easy to reach',
  FairPrice: 'Fair price',
  Late: 'Late',
  BelowExpectations: 'Below expectations',
}

export function ratingTagLabel(t: SupplierRatingTag): string {
  return tagLabels[t]
}
