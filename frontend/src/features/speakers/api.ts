import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiVoid } from '@/app/api'
import {
  sendUnsentResultSchema,
  speakerDetailSchema,
  speakersListSchema,
  type TravelModeValue,
} from './schemas'

const enc = encodeURIComponent

export const speakersKeys = {
  all: (eventId: string) => ['agenda-speakers', eventId] as const,
  list: (eventId: string) => ['agenda-speakers', eventId, 'list'] as const,
  detail: (eventId: string, sid: string) => ['agenda-speakers', eventId, 'detail', sid] as const,
}

export function useSpeakersListQuery(eventId: string) {
  return useQuery({
    queryKey: speakersKeys.list(eventId),
    queryFn: () => api(`/api/events/${enc(eventId)}/agenda/speakers`, speakersListSchema),
    enabled: !!eventId,
  })
}

export function useSpeakerDetailQuery(eventId: string, sid: string | null) {
  return useQuery({
    queryKey: speakersKeys.detail(eventId, sid ?? ''),
    queryFn: () => api(`/api/events/${enc(eventId)}/agenda/speakers/${enc(sid ?? '')}`, speakerDetailSchema),
    enabled: !!eventId && !!sid,
  })
}

function useSpeakersSettled(eventId: string) {
  const qc = useQueryClient()
  return () => qc.invalidateQueries({ queryKey: speakersKeys.all(eventId) })
}

export function useSpeakerActionMutation(eventId: string) {
  const settled = useSpeakersSettled(eventId)
  return useMutation({
    mutationFn: ({ sid, action, body }: { sid: string; action: string; body?: Record<string, unknown> }) =>
      api(`/api/events/${enc(eventId)}/agenda/speakers/${enc(sid)}/${action}`, speakerDetailSchema, { method: 'POST', body }),
    onSuccess: settled,
  })
}

export function useApprovePhotoMutation(eventId: string) {
  const settled = useSpeakersSettled(eventId)
  return useMutation({
    mutationFn: (sid: string) => api(`/api/events/${enc(eventId)}/agenda/speakers/${enc(sid)}/photo/approve`, speakerDetailSchema, { method: 'POST' }),
    onSuccess: settled,
  })
}

export function useSendUnsentMutation(eventId: string) {
  const settled = useSpeakersSettled(eventId)
  return useMutation({
    mutationFn: () => api(`/api/events/${enc(eventId)}/agenda/speakers/links/send-unsent`, sendUnsentResultSchema, { method: 'POST' }),
    onSuccess: settled,
  })
}

export function useSetTravelModeMutation(eventId: string) {
  const settled = useSpeakersSettled(eventId)
  return useMutation({
    mutationFn: (travelMode: TravelModeValue) => apiVoid(`/api/events/${enc(eventId)}/travel-mode`, { method: 'PUT', body: { travelMode } }),
    onSuccess: settled,
  })
}
