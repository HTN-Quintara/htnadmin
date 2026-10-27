import { z } from 'zod'

export const leadKinds = ['Sponsor', 'Speaker'] as const
export const leadStatuses = ['Prospect', 'Contacted', 'InTalks', 'Confirmed', 'Declined'] as const
export type LeadKind = (typeof leadKinds)[number]
export type LeadStatus = (typeof leadStatuses)[number]

export const leadCardSchema = z.object({
  id: z.string(),
  kind: z.enum(leadKinds),
  status: z.enum(leadStatuses),
  subject: z.string(),
  contactName: z.string().nullable(),
  contactRole: z.string().nullable(),
  contactEmail: z.string().nullable(),
  contactMobile: z.string().nullable(),
  askTierId: z.string().nullable(),
  askTierName: z.string().nullable(),
  askAmount: z.number().nullable(),
  askNote: z.string().nullable(),
  ask: z.string(),
  ownerPersonId: z.string().nullable(),
  ownerName: z.string().nullable(),
  ownerInitials: z.string().nullable(),
  nextStep: z.string().nullable(),
  nextStepDue: z.string().nullable(),
  overdue: z.boolean(),
  fromDirectory: z.boolean(),
  sponsorId: z.string().nullable(),
  speakerId: z.string().nullable(),
})
export type LeadCard = z.infer<typeof leadCardSchema>

export const leadColumnSchema = z.object({
  status: z.enum(leadStatuses),
  name: z.string(),
  count: z.number(),
  cards: z.array(leadCardSchema),
})

export const leadStatSchema = z.object({ label: z.string(), value: z.string() })

export const leadsBoardSchema = z.object({
  kind: z.enum(leadKinds),
  stats: z.array(leadStatSchema),
  columns: z.array(leadColumnSchema),
})
export type LeadsBoard = z.infer<typeof leadsBoardSchema>

export const confirmResultSchema = z.object({
  leadId: z.string(),
  sponsorId: z.string().nullable(),
  speakerId: z.string().nullable(),
})

export const leadFormSchema = z.object({
  kind: z.enum(leadKinds),
  subject: z.string().trim().min(1, 'Give a name.').max(160),
  contactName: z.string().trim().max(120).optional().or(z.literal('')),
  contactRole: z.string().trim().max(80).optional().or(z.literal('')),
  contactEmail: z.string().trim().email('Enter a valid email.').max(254).optional().or(z.literal('')),
  askAmount: z.string().trim().optional().or(z.literal('')),
  askNote: z.string().trim().max(120).optional().or(z.literal('')),
  nextStep: z.string().trim().max(200).optional().or(z.literal('')),
  nextStepDue: z.string().trim().optional().or(z.literal('')),
})
export type LeadFormValues = z.infer<typeof leadFormSchema>
