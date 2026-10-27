import { z } from 'zod'

/** Mirrors Htn.Domain.Invitations.InvitationStatus. "Manual CRM-style": only Sent is automatic. */
export const invitationStatuses = ['NotSent', 'Sent', 'Opened', 'Replied', 'Confirmed', 'Declined', 'Bounced'] as const
export type InvitationStatus = (typeof invitationStatuses)[number]

/** The transition whitelist mirrored from EventInvitation.Transitions (backend remains the real gate). */
export const invitationTransitions: Record<InvitationStatus, InvitationStatus[]> = {
  NotSent: [],
  Sent: ['Opened', 'Replied', 'Confirmed', 'Declined', 'Bounced'],
  Opened: ['Replied', 'Confirmed', 'Declined', 'Bounced'],
  Replied: ['Confirmed', 'Declined', 'Bounced'],
  Confirmed: [],
  Declined: [],
  Bounced: ['Sent'],
}

export const invitationListItemSchema = z.object({
  id: z.string(),
  directoryOrgId: z.string(),
  orgName: z.string(),
  orgKind: z.string(),
  status: z.enum(invitationStatuses),
  sentAt: z.string().nullable(),
  lastStatusAt: z.string().nullable(),
  primaryContactVerified: z.boolean(),
})
export type InvitationListItem = z.infer<typeof invitationListItemSchema>

export const invitationsListSchema = z.array(invitationListItemSchema)

export const invitationTemplateSchema = z.object({
  eventId: z.string(),
  subject: z.string(),
  body: z.string(),
  updatedAt: z.string(),
})
export type InvitationTemplate = z.infer<typeof invitationTemplateSchema>

export const createInvitationsResultSchema = z.object({
  created: z.array(z.string()),
  skipped: z.array(z.string()),
})

export const invitationPreviewSchema = z.object({
  subject: z.string(),
  body: z.string(),
})
export type InvitationPreview = z.infer<typeof invitationPreviewSchema>

export const invitationEventSchema = z.object({
  id: z.string(),
  kind: z.enum(invitationStatuses),
  at: z.string(),
  byId: z.string().nullable(),
  note: z.string().nullable(),
})

export const invitationDetailSchema = z.object({
  id: z.string(),
  eventId: z.string(),
  directoryOrgId: z.string(),
  orgName: z.string(),
  status: z.enum(invitationStatuses),
  contactId: z.string().nullable(),
  sentAt: z.string().nullable(),
  lastStatusAt: z.string().nullable(),
  letterSubjectSnapshot: z.string().nullable(),
  letterBodySnapshot: z.string().nullable(),
  history: z.array(invitationEventSchema),
})
export type InvitationDetail = z.infer<typeof invitationDetailSchema>

// ---- Form schemas ----

export const templateFormSchema = z.object({
  subject: z.string().trim().min(1, 'Give a subject.').max(200),
  body: z.string().trim().min(1, 'Write the letter body.').max(8000),
})
export type TemplateFormValues = z.infer<typeof templateFormSchema>

/** Literal merge tokens the body/subject may carry — shown as a reference next to the editor. */
export const mergeTokens = ['[date]', '[addressee.name]', '[addressee.position]', '[addressee.salutation]'] as const
