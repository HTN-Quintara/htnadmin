import { z } from 'zod'

export const directoryOrgKinds = ['Government', 'Company', 'University', 'NonProfit'] as const
export type DirectoryOrgKind = (typeof directoryOrgKinds)[number]

export const directoryContactHistoryKinds = ['HolderChanged', 'Verified', 'StillCorrect', 'Bounced'] as const
export type DirectoryContactHistoryKind = (typeof directoryContactHistoryKinds)[number]

export const directoryOrgSchema = z.object({
  id: z.string(),
  kind: z.enum(directoryOrgKinds),
  name: z.string(),
  nameKey: z.string(),
  addresseeSalutation: z.string().nullable(),
  websiteUrl: z.string().nullable(),
  logoFileId: z.string().nullable(),
  notes: z.string().nullable(),
  isActive: z.boolean(),
  needsCheck: z.boolean(),
})
export type DirectoryOrg = z.infer<typeof directoryOrgSchema>

export const directoryContactSchema = z.object({
  id: z.string(),
  orgId: z.string(),
  position: z.string(),
  holderName: z.string(),
  email: z.string(),
  phone: z.string().nullable(),
  isPrimary: z.boolean(),
  verifiedAt: z.string().nullable(),
  needsCheck: z.boolean(),
  ccEmails: z.array(z.string()),
})
export type DirectoryContact = z.infer<typeof directoryContactSchema>

export const directoryContactHistorySchema = z.object({
  id: z.string(),
  contactId: z.string().nullable(),
  kind: z.enum(directoryContactHistoryKinds),
  at: z.string(),
  byId: z.string().nullable(),
  note: z.string().nullable(),
  priorPosition: z.string().nullable(),
  priorHolderName: z.string().nullable(),
})
export type DirectoryContactHistory = z.infer<typeof directoryContactHistorySchema>

export const directoryOrgsListSchema = z.array(directoryOrgSchema)

export const directoryOrgDetailSchema = z.object({
  org: directoryOrgSchema,
  contacts: z.array(directoryContactSchema),
  history: z.array(directoryContactHistorySchema),
})
export type DirectoryOrgDetail = z.infer<typeof directoryOrgDetailSchema>

// ---- Form schemas ----

export const orgFormSchema = z.object({
  kind: z.enum(directoryOrgKinds),
  name: z.string().trim().min(1, 'Give a name.').max(160),
  addresseeSalutation: z.string().trim().max(120).optional().or(z.literal('')),
  websiteUrl: z.string().trim().max(300).optional().or(z.literal('')),
  notes: z.string().trim().max(1000).optional().or(z.literal('')),
})
export type OrgFormValues = z.infer<typeof orgFormSchema>

export const contactFormSchema = z.object({
  position: z.string().trim().min(1, 'Give a position.').max(80),
  holderName: z.string().trim().min(1, 'Give a holder name.').max(120),
  email: z.string().trim().min(1, 'Give an email.').email('Enter a valid email.').max(254),
  phone: z.string().trim().max(20).optional().or(z.literal('')),
  ccEmails: z.string().trim().max(1000).optional().or(z.literal('')),
  isPrimary: z.boolean(),
})
export type ContactFormValues = z.infer<typeof contactFormSchema>

export const headChangedFormSchema = z.object({
  position: z.string().trim().min(1, 'Give a position.').max(80),
  holderName: z.string().trim().min(1, 'Give the new holder’s name.').max(120),
  email: z.string().trim().min(1, 'Give an email.').email('Enter a valid email.').max(254),
  phone: z.string().trim().max(20).optional().or(z.literal('')),
})
export type HeadChangedFormValues = z.infer<typeof headChangedFormSchema>
