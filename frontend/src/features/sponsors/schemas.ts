import { z } from 'zod'

export const sponsorKinds = ['Organization', 'Individual'] as const
export const sponsorStatuses = ['AwaitingContract', 'Confirmed'] as const
export const tierClasses = ['Platinum', 'Gold', 'Silver', 'Partner', 'Individual'] as const
export const countBases = ['PaidOnly', 'AllRegistrants', 'CheckedIn'] as const

export type SponsorKind = (typeof sponsorKinds)[number]
export type SponsorStatus = (typeof sponsorStatuses)[number]
export type TierClass = (typeof tierClasses)[number]
export type CountBasis = (typeof countBases)[number]

export const kpiSchema = z.object({ label: z.string(), value: z.string(), sub: z.string().nullable() })

export const tierSchema = z.object({
  id: z.string(),
  class: z.enum(tierClasses),
  name: z.string(),
  priceText: z.string(),
  slots: z.number(),
  slotsTaken: z.number(),
  compedSeats: z.number(),
  perks: z.array(z.string()),
  isActive: z.boolean(),
})
export type SponsorTier = z.infer<typeof tierSchema>

export const perkSchema = z.object({
  id: z.string(),
  text: z.string(),
  delivered: z.boolean(),
  removed: z.boolean(),
  sortOrder: z.number(),
})
export type SponsorPerk = z.infer<typeof perkSchema>

export const sponsorSchema = z.object({
  id: z.string(),
  kind: z.enum(sponsorKinds),
  name: z.string(),
  displayName: z.string().nullable(),
  isAnonymous: z.boolean(),
  contactName: z.string().nullable(),
  contactRole: z.string().nullable(),
  contactEmail: z.string().nullable(),
  contactMobile: z.string().nullable(),
  status: z.enum(sponsorStatuses),
  tierId: z.string().nullable(),
  tierClass: z.enum(tierClasses).nullable(),
  tierName: z.string().nullable(),
  contributionType: z.string(),
  cashAmount: z.number().nullable(),
  inKindDescription: z.string().nullable(),
  inKindItemCount: z.number().nullable(),
  amountText: z.string(),
  logoFileId: z.string().nullable(),
  websiteUrl: z.string().nullable(),
  needsAssets: z.boolean(),
  compsUsed: z.number(),
  compsEntitlement: z.number(),
  perksDelivered: z.number(),
  perksTotal: z.number(),
  cashReceived: z.boolean(),
  cashReceivedAt: z.string().nullable(),
  cashReference: z.string().nullable(),
})
export type Sponsor = z.infer<typeof sponsorSchema>

export const sponsorsPageSchema = z.object({
  eventId: z.string(),
  eventName: z.string(),
  kpis: z.array(kpiSchema),
  tiers: z.array(tierSchema),
  sponsors: z.array(sponsorSchema),
})
export type SponsorsPage = z.infer<typeof sponsorsPageSchema>

export const sponsorDetailSchema = z.object({ sponsor: sponsorSchema, perks: z.array(perkSchema) })
export type SponsorDetail = z.infer<typeof sponsorDetailSchema>

export const partnerSettingsSchema = z.object({
  threshold: z.number(),
  countBasis: z.enum(countBases),
  groupLinkEnabled: z.boolean(),
})
export type PartnerSettings = z.infer<typeof partnerSettingsSchema>

export const partnerSchema = z.object({
  id: z.string().nullable(),
  nameKey: z.string(),
  displayName: z.string(),
  orgType: z.string().nullable(),
  registered: z.number(),
  paid: z.number(),
  checkedIn: z.number(),
  countForBasis: z.number(),
  status: z.string(),
  remaining: z.number(),
  isPartner: z.boolean(),
  isTagged: z.boolean(),
  assets: z.string(),
  logoFileId: z.string().nullable(),
  websiteUrl: z.string().nullable(),
  hasGroupLink: z.boolean(),
  groupJoined: z.number(),
})
export type Partner = z.infer<typeof partnerSchema>

export const unmatchedSchema = z.object({
  nameKey: z.string(),
  sampleName: z.string(),
  registered: z.number(),
  paid: z.number(),
})
export type Unmatched = z.infer<typeof unmatchedSchema>

export const partnersPageSchema = z.object({
  settings: partnerSettingsSchema,
  partners: z.array(partnerSchema),
  unmatched: z.array(unmatchedSchema),
  partnerPerks: z.array(z.string()),
})
export type PartnersPage = z.infer<typeof partnersPageSchema>

export const groupLinkSchema = z.object({ url: z.string() })

// ---- Form schemas ----

export const sponsorFormSchema = z.object({
  kind: z.enum(sponsorKinds),
  name: z.string().trim().min(1, 'Give a name.').max(160),
  displayName: z.string().trim().max(160).optional().or(z.literal('')),
  isAnonymous: z.boolean(),
  contactName: z.string().trim().max(120).optional().or(z.literal('')),
  contactRole: z.string().trim().max(80).optional().or(z.literal('')),
  contactEmail: z.string().trim().email('Enter a valid email.').max(254).optional().or(z.literal('')),
  tierId: z.string().optional().or(z.literal('')),
  cashAmount: z.string().trim().optional().or(z.literal('')),
  inKindDescription: z.string().trim().max(300).optional().or(z.literal('')),
  inKindItemCount: z.string().trim().optional().or(z.literal('')),
  websiteUrl: z.string().trim().max(300).optional().or(z.literal('')),
})
export type SponsorFormValues = z.infer<typeof sponsorFormSchema>

export const tierFormSchema = z.object({
  class: z.enum(tierClasses),
  name: z.string().trim().min(1, 'Give a name.').max(80),
  cashAmount: z.string().trim().optional().or(z.literal('')),
  priceNote: z.string().trim().max(80).optional().or(z.literal('')),
  inKindText: z.string().trim().max(200).optional().or(z.literal('')),
  inKindItemCount: z.string().trim().optional().or(z.literal('')),
  slots: z.string().trim(),
  compedSeats: z.string().trim(),
  perks: z.string().trim().optional().or(z.literal('')),
})
export type TierFormValues = z.infer<typeof tierFormSchema>
