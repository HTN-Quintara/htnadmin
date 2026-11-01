import { z } from 'zod'

/** Mirrors `Htn.Domain.Merch.MerchSaleMethod` — enum members serialize by their C# name verbatim. */
export const merchSaleMethods = ['Cash', 'GCash', 'Bank'] as const
export type MerchSaleMethod = (typeof merchSaleMethods)[number]

/** `Htn.Domain.Organization.PersonLevel`, plus "Attendee" for an unresolved buyer (POS search/scan only). */
export const buyerLevels = ['Board', 'Goon', 'Apprentice', 'Member', 'Attendee'] as const
export type BuyerLevel = (typeof buyerLevels)[number]

export const merchVariantSchema = z.object({
  id: z.string(),
  itemId: z.string(),
  label: z.string(),
  stockOnHand: z.number(),
  isActive: z.boolean(),
  lowStock: z.boolean(),
})
export type MerchVariant = z.infer<typeof merchVariantSchema>

export const merchItemSchema = z.object({
  id: z.string(),
  // Phase 8C: null once "Move leftovers to store" unlinks the item from its event into the shared leftover pool.
  eventId: z.string().nullable(),
  leftoverSourceEventId: z.string().nullable(),
  name: z.string(),
  price: z.number(),
  swatchColor: z.string().nullable(),
  isActive: z.boolean(),
  variants: z.array(merchVariantSchema),
})
export type MerchItem = z.infer<typeof merchItemSchema>

export const eventMerchSettingsSchema = z.object({
  eventId: z.string(),
  goonPct: z.number(),
  apprenticePct: z.number(),
  memberPct: z.number(),
  boardPct: z.number(),
})
export type EventMerchSettings = z.infer<typeof eventMerchSettingsSchema>

export const merchSaleLineSchema = z.object({
  id: z.string(),
  itemId: z.string(),
  itemName: z.string(),
  variantId: z.string(),
  variantLabel: z.string(),
  qty: z.number(),
  unitPriceSnapshot: z.number(),
  lineTotal: z.number(),
})
export type MerchSaleLine = z.infer<typeof merchSaleLineSchema>

export const merchSaleSchema = z.object({
  id: z.string(),
  // Phase 8C: exactly one of eventId/popUpId is set — a pop-up sale always posts to the Organization fund.
  eventId: z.string().nullable(),
  popUpId: z.string().nullable().default(null),
  buyerPersonId: z.string().nullable(),
  buyerAttendeeId: z.string().nullable(),
  buyerNameSnapshot: z.string(),
  buyerLevelSnapshot: z.enum(['Board', 'Goon', 'Apprentice', 'Member']).nullable(),
  subtotal: z.number(),
  discountPct: z.number(),
  discountAmount: z.number(),
  total: z.number(),
  method: z.enum(merchSaleMethods),
  cashierId: z.string(),
  station: z.string().nullable(),
  soldAt: z.string(),
  lines: z.array(merchSaleLineSchema),
  // Phase 8B: the "Membership {year}" POS tile. The fee is a flat yearly dues amount, never discounted,
  // added on top of the (already-discounted) merch subtotal — see `Htn.Domain.Merch.MerchSale`.
  membershipFeeIncluded: z.boolean().default(false),
  membershipFeeAmount: z.number().nullable().default(null),
})
export type MerchSale = z.infer<typeof merchSaleSchema>

export const merchKpiSchema = z.object({
  orderCount: z.number(),
  itemsSold: z.number(),
  itemsInStock: z.number(),
  lowStockVariantCount: z.number(),
  totalSales: z.number().nullable(),
  discountsGiven: z.number().nullable(),
})
export type MerchKpi = z.infer<typeof merchKpiSchema>

export const merchDashboardSchema = z.object({
  canManage: z.boolean(),
  kpi: merchKpiSchema,
  items: z.array(merchItemSchema),
  sales: z.array(merchSaleSchema),
})
export type MerchDashboard = z.infer<typeof merchDashboardSchema>

export const buyerHitSchema = z.object({
  kind: z.enum(['Person', 'Attendee']),
  personId: z.string().nullable(),
  attendeeId: z.string().nullable(),
  name: z.string(),
  level: z.enum(buyerLevels),
  email: z.string().nullable(),
})
export type BuyerHit = z.infer<typeof buyerHitSchema>

export const buyerHitsSchema = z.array(buyerHitSchema)

// ---- Form schemas ----

const variantRowSchema = z.object({
  label: z.string().trim().min(1, 'Give this variant a label.').max(40),
  stockOnHand: z.coerce.number().int('Whole numbers only.').min(0, 'Stock cannot be negative.').max(1_000_000),
})

export const itemFormSchema = z.object({
  name: z.string().trim().min(1, 'Give the item a name.').max(160),
  price: z.coerce.number().min(0, 'Price cannot be negative.'),
  swatchColor: z.string().trim().min(1, 'Pick a color.'),
  variants: z.array(variantRowSchema).min(1, 'Add at least one variant.'),
})
export type ItemFormInput = z.input<typeof itemFormSchema>
export type ItemFormValues = z.output<typeof itemFormSchema>

export const discountsFormSchema = z.object({
  goonPct: z.number().int().min(0).max(100),
  apprenticePct: z.number().int().min(0).max(100),
  memberPct: z.number().int().min(0).max(100),
  boardPct: z.number().int().min(0).max(100),
})
export type DiscountsFormValues = z.infer<typeof discountsFormSchema>

// ---- Pop-up stores (Phase 8C, PopUpContracts.cs) ----

/** Mirrors `Htn.Domain.PopUps.PopUpStatus` — forward-only, no backward edge. */
export const popUpStatuses = ['Planned', 'Open', 'NeedsCount', 'ClosedBalanced', 'ClosedMissing'] as const
export type PopUpStatus = (typeof popUpStatuses)[number]

/** Mirrors `Htn.Domain.PopUps.PopUpDiscountMode`. */
export const popUpDiscountModes = ['SameAsDefault', 'None', 'Custom'] as const
export type PopUpDiscountMode = (typeof popUpDiscountModes)[number]

export const popUpTeamMemberSchema = z.object({
  personId: z.string(),
  name: z.string(),
})
export type PopUpTeamMember = z.infer<typeof popUpTeamMemberSchema>

export const popUpAllocationSchema = z.object({
  variantId: z.string(),
  itemName: z.string(),
  variantLabel: z.string(),
  qtyBrought: z.number(),
  qtySold: z.number(),
  qtyReturned: z.number().nullable(),
  qtyMissing: z.number().nullable(),
})
export type PopUpAllocation = z.infer<typeof popUpAllocationSchema>

export const popUpSchema = z.object({
  id: z.string(),
  name: z.string(),
  venueDirectoryOrgId: z.string().nullable(),
  venueOrgName: z.string().nullable(),
  venueText: z.string().nullable(),
  startsAt: z.string(),
  endsAt: z.string(),
  feeAmount: z.number(),
  discountMode: z.enum(popUpDiscountModes),
  goonPct: z.number().nullable(),
  apprenticePct: z.number().nullable(),
  memberPct: z.number().nullable(),
  boardPct: z.number().nullable(),
  ownerCommitteeId: z.string(),
  ownerCommitteeName: z.string(),
  status: z.enum(popUpStatuses),
  team: z.array(popUpTeamMemberSchema),
  allocations: z.array(popUpAllocationSchema),
  totalSales: z.number().nullable(),
  discountsGiven: z.number().nullable(),
  cashCounted: z.number().nullable(),
  missingValue: z.number().nullable(),
  isWrittenOff: z.boolean(),
  writtenOffAt: z.string().nullable(),
  canManage: z.boolean(),
  canSell: z.boolean(),
})
export type PopUp = z.infer<typeof popUpSchema>

export const popUpsListSchema = z.array(popUpSchema)

export const leftoverVariantSchema = z.object({
  itemId: z.string(),
  itemName: z.string(),
  variantId: z.string(),
  variantLabel: z.string(),
  price: z.number(),
  fromEventId: z.string().nullable(),
  fromEventName: z.string(),
  stockOnHand: z.number(),
  reserved: z.number(),
  available: z.number(),
})
export type LeftoverVariant = z.infer<typeof leftoverVariantSchema>

export const leftoverInventorySchema = z.array(leftoverVariantSchema)

// ---- Pop-up forms ----

const customDiscountSchema = z.object({
  goonPct: z.coerce.number().int().min(0).max(100),
  apprenticePct: z.coerce.number().int().min(0).max(100),
  memberPct: z.coerce.number().int().min(0).max(100),
  boardPct: z.coerce.number().int().min(0).max(100),
})

export const newPopUpFormSchema = z
  .object({
    name: z.string().trim().min(1, 'Give the pop-up a name.').max(160),
    venueKind: z.enum(['org', 'text']),
    venueDirectoryOrgId: z.string().trim().optional(),
    venueText: z.string().trim().max(200).optional(),
    startsAt: z.string().min(1, 'Pick a start date and time.'),
    endsAt: z.string().min(1, 'Pick an end date and time.'),
    feeAmount: z.coerce.number().min(0, 'Fee cannot be negative.'),
    discountMode: z.enum(popUpDiscountModes),
    custom: customDiscountSchema,
    ownerCommitteeId: z.string().trim().min(1, 'Pick the committee running this pop-up.'),
    teamPersonIds: z.array(z.string()).default([]),
  })
  .refine((v) => (v.venueKind === 'org' ? !!v.venueDirectoryOrgId : !!v.venueText && v.venueText.length > 0), {
    message: 'Pick a venue organization, or type one in.',
    path: ['venueText'],
  })
  .refine((v) => new Date(v.endsAt).getTime() > new Date(v.startsAt).getTime(), {
    message: 'End must be after the start.',
    path: ['endsAt'],
  })
export type NewPopUpFormInput = z.input<typeof newPopUpFormSchema>
export type NewPopUpFormValues = z.output<typeof newPopUpFormSchema>

const allocationLineSchema = z.object({
  variantId: z.string(),
  itemName: z.string(),
  variantLabel: z.string(),
  available: z.number(),
  qty: z.coerce.number().int('Whole numbers only.').min(0, 'Cannot be negative.'),
})
export const allocationFormSchema = z.object({
  lines: z.array(allocationLineSchema),
})
export type AllocationFormInput = z.input<typeof allocationFormSchema>
export type AllocationFormValues = z.output<typeof allocationFormSchema>

const countLineSchema = z.object({
  variantId: z.string(),
  itemName: z.string(),
  variantLabel: z.string(),
  max: z.number(),
  qtyReturned: z.coerce.number().int('Whole numbers only.').min(0, 'Cannot be negative.'),
})
export const countFormSchema = z.object({
  lines: z.array(countLineSchema),
  cashCounted: z.coerce.number().min(0, 'Cannot be negative.'),
})
export type CountFormInput = z.input<typeof countFormSchema>
export type CountFormValues = z.output<typeof countFormSchema>

export const writeOffFormSchema = z.object({
  reason: z.string().trim().min(1, 'Enter a reason.').max(500),
})
export type WriteOffFormValues = z.infer<typeof writeOffFormSchema>
