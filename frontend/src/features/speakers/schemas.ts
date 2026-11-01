import { z } from 'zod'

export const travelModes = ['None', 'Plane', 'Bus', 'PlaneOrBus'] as const
export type TravelModeValue = (typeof travelModes)[number]

export const kpiSchema = z.object({ label: z.string(), value: z.string() })

export const speakerRowSchema = z.object({
  id: z.string(),
  name: z.string(),
  session: z.string(),
  photoState: z.string(),
  linkState: z.string(),
  linkStateSub: z.string().nullable(),
  dates: z.string(),
  travel: z.string(),
  plusOne: z.string(),
})
export type SpeakerRow = z.infer<typeof speakerRowSchema>

export const speakersListSchema = z.object({
  travelMode: z.enum(travelModes),
  kpis: z.array(kpiSchema),
  speakers: z.array(speakerRowSchema),
})
export type SpeakersList = z.infer<typeof speakersListSchema>

export const logSchema = z.object({ at: z.string(), what: z.string() })

export const plusOneSchema = z.object({
  id: z.string(), name: z.string(), email: z.string(), sharesRoom: z.boolean(), voucherCode: z.string(), registered: z.boolean(),
})

export const travelDtoSchema = z.object({
  arrivalDate: z.string().nullable(),
  departureDate: z.string().nullable(),
  flyingFrom: z.string().nullable(),
  preferredArrivalTime: z.string().nullable(),
  hasIdFullName: z.boolean(),
  busFrom: z.string().nullable(),
  busEta: z.string().nullable(),
  needsTerminalPickup: z.boolean(),
  roomNote: z.string().nullable(),
  needsNotes: z.string().nullable(),
})

export const speakerDetailSchema = z.object({
  id: z.string(),
  name: z.string(),
  email: z.string(),
  affiliation: z.string().nullable(),
  status: z.string(),
  session: z.string(),
  photoState: z.string(),
  canApprovePhoto: z.boolean(),
  travel: travelDtoSchema.nullable(),
  plusOne: plusOneSchema.nullable(),
  log: z.array(logSchema),
  canSend: z.boolean(),
  sent: z.boolean(),
})
export type SpeakerDetail = z.infer<typeof speakerDetailSchema>

export const sendUnsentResultSchema = z.object({ sent: z.number() })

// ---- Public form ----

export const speakerLandingSchema = z.object({
  eventName: z.string(),
  eventStartsAt: z.string(),
  speakerFirstName: z.string(),
  maskedEmail: z.string(),
  travelMode: z.enum(travelModes),
  expiresAt: z.string(),
  state: z.enum(['NeedsCode', 'Submitted']),
})
export type SpeakerLanding = z.infer<typeof speakerLandingSchema>

export const speakerFormStateSchema = z.object({
  speakerName: z.string(),
  session: z.string(),
  travelMode: z.enum(travelModes),
  photoFileId: z.string().nullable(),
  photoApproved: z.boolean(),
  arrivalDate: z.string().nullable(),
  departureDate: z.string().nullable(),
  flyingFrom: z.string().nullable(),
  preferredArrivalTime: z.string().nullable(),
  busFrom: z.string().nullable(),
  busEta: z.string().nullable(),
  needsTerminalPickup: z.boolean(),
  needsNotes: z.string().nullable(),
  hasPlusOne: z.boolean(),
  plusOneName: z.string().nullable(),
  plusOneEmail: z.string().nullable(),
  plusOneSharesRoom: z.boolean(),
  submitted: z.boolean(),
})
export type SpeakerFormState = z.infer<typeof speakerFormStateSchema>

export const verifyResultSchema = z.object({
  sessionToken: z.string(),
  sessionExpiresAt: z.string(),
  form: speakerFormStateSchema,
})

export const submitResultSchema = z.object({ submittedAt: z.string(), plusOneVoucherCode: z.string().nullable() })

export const photoUploadResultSchema = z.object({ fileId: z.string() })
