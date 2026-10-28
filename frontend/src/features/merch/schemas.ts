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
  eventId: z.string(),
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
  eventId: z.string(),
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
