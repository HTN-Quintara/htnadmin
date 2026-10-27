import { z } from 'zod'
import { api, apiVoid } from '@/app/api'
import {
  speakerFormStateSchema,
  speakerLandingSchema,
  submitResultSchema,
  verifyResultSchema,
  type SpeakerFormState,
} from '../schemas'

const enc = encodeURIComponent
const base = (token: string) => `/api/public/speaker-links/${enc(token)}`

export const getLanding = (token: string) => api(base(token), speakerLandingSchema)

export const requestCode = (token: string) => apiVoid(`${base(token)}/code`, { method: 'POST' })

export const verifyCode = (token: string, code: string) =>
  api(`${base(token)}/verify`, verifyResultSchema, { method: 'POST', body: { code } })

export const getForm = (token: string, sessionToken: string) =>
  api(`${base(token)}/form`, speakerFormStateSchema, { headers: { 'X-Speaker-Session': sessionToken } })

export type SubmitBody = {
  arrivalDate?: string | null
  departureDate?: string | null
  flyingFrom?: string | null
  preferredArrivalTime?: string | null
  idFullName?: string | null
  busFrom?: string | null
  busEta?: string | null
  needsTerminalPickup?: boolean
  needsNotes?: string | null
  consent: boolean
  hasPlusOne: boolean
  plusOneName?: string | null
  plusOneEmail?: string | null
  plusOneSharesRoom?: boolean
}

export const submitForm = (token: string, sessionToken: string, body: SubmitBody) =>
  api(`${base(token)}/submit`, submitResultSchema, { method: 'POST', body, headers: { 'X-Speaker-Session': sessionToken } })

const photoResultSchema = z.object({ fileId: z.string() })

export function uploadPhoto(token: string, sessionToken: string, file: File) {
  const form = new FormData()
  form.append('file', file)
  return api(`${base(token)}/photo`, photoResultSchema, {
    method: 'POST',
    body: form,
    headers: { 'X-Speaker-Session': sessionToken },
  })
}

export type { SpeakerFormState }
