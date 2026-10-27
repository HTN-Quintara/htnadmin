import { z } from 'zod'

export const sessionKinds = ['Keynote', 'Talk', 'Panel', 'Workshop', 'Break'] as const
export const speakerRoles = ['Speaker', 'Moderator', 'Panellist'] as const
export type SessionKind = (typeof sessionKinds)[number]
export type SpeakerRole = (typeof speakerRoles)[number]

export const roomSchema = z.object({
  id: z.string(),
  name: z.string(),
  colorHex: z.string(),
  sortOrder: z.number(),
  isActive: z.boolean(),
})
export type Room = z.infer<typeof roomSchema>

export const sessionSpeakerSchema = z.object({
  speakerId: z.string(),
  name: z.string(),
  role: z.string(),
  sortOrder: z.number(),
  tentative: z.boolean(),
})

export const sessionSchema = z.object({
  id: z.string(),
  title: z.string(),
  kind: z.enum(sessionKinds),
  startsAt: z.string(),
  durationMinutes: z.number(),
  time: z.string(),
  ownerCommitteeId: z.string().nullable(),
  isPublic: z.boolean(),
  notes: z.string().nullable(),
  rooms: z.array(roomSchema),
  speakers: z.array(sessionSpeakerSchema),
  tentative: z.boolean(),
})
export type Session = z.infer<typeof sessionSchema>

export const agendaSlotSchema = z.object({ time: z.string(), sessions: z.array(sessionSchema) })

export const agendaDaySchema = z.object({
  date: z.string(),
  heading: z.string(),
  sessionCount: z.number(),
  tentativeSpeakers: z.number(),
  slots: z.array(agendaSlotSchema),
})

export const agendaSchema = z.object({
  eventId: z.string(),
  eventName: z.string(),
  rooms: z.array(roomSchema),
  days: z.array(agendaDaySchema),
  canManage: z.boolean(),
})
export type Agenda = z.infer<typeof agendaSchema>

export const speakerSummarySchema = z.object({ id: z.string(), name: z.string(), status: z.string() })
export type SpeakerSummary = z.infer<typeof speakerSummarySchema>

export const sessionFormSchema = z.object({
  title: z.string().trim().min(1, 'Give a title.').max(160),
  kind: z.enum(sessionKinds),
  date: z.string().trim().min(1, 'Pick a date.'),
  time: z.string().trim().min(1, 'Pick a start time.'),
  durationMinutes: z.string().trim(),
  isPublic: z.boolean(),
  notes: z.string().trim().max(500).optional().or(z.literal('')),
})
export type SessionFormValues = z.infer<typeof sessionFormSchema>
