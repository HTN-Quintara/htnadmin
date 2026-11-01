import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiVoid } from '@/app/api'
import { z } from 'zod'
import { agendaSchema, roomSchema, sessionSchema, speakerSummarySchema } from './schemas'

const enc = encodeURIComponent

export const agendaKeys = {
  all: (eventId: string) => ['agenda', eventId] as const,
  agenda: (eventId: string) => ['agenda', eventId, 'programme'] as const,
  speakers: (eventId: string) => ['agenda', eventId, 'speaker-options'] as const,
}

export function useAgendaQuery(eventId: string) {
  return useQuery({ queryKey: agendaKeys.agenda(eventId), queryFn: () => api(`/api/events/${enc(eventId)}/agenda`, agendaSchema), enabled: !!eventId })
}

export function useSpeakerOptionsQuery(eventId: string, enabled: boolean) {
  return useQuery({
    queryKey: agendaKeys.speakers(eventId),
    queryFn: () => api(`/api/events/${enc(eventId)}/speakers`, z.array(speakerSummarySchema)),
    enabled: !!eventId && enabled,
  })
}

function useAgendaSettled(eventId: string) {
  const qc = useQueryClient()
  return () => qc.invalidateQueries({ queryKey: agendaKeys.all(eventId) })
}

export type AgendaBody = Record<string, unknown>

export function useSaveSessionMutation(eventId: string) {
  const settled = useAgendaSettled(eventId)
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: AgendaBody }) =>
      api(id ? `/api/events/${enc(eventId)}/sessions/${enc(id)}` : `/api/events/${enc(eventId)}/sessions`, sessionSchema, {
        method: id ? 'PUT' : 'POST', body,
      }),
    onSuccess: settled,
  })
}

export function useDeleteSessionMutation(eventId: string) {
  const settled = useAgendaSettled(eventId)
  return useMutation({
    mutationFn: (id: string) => apiVoid(`/api/events/${enc(eventId)}/sessions/${enc(id)}`, { method: 'DELETE' }),
    onSuccess: settled,
  })
}

export function useSaveRoomMutation(eventId: string) {
  const settled = useAgendaSettled(eventId)
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: AgendaBody }) =>
      api(id ? `/api/events/${enc(eventId)}/rooms/${enc(id)}` : `/api/events/${enc(eventId)}/rooms`, roomSchema, {
        method: id ? 'PUT' : 'POST', body,
      }),
    onSuccess: settled,
  })
}

export function useDeactivateRoomMutation(eventId: string) {
  const settled = useAgendaSettled(eventId)
  return useMutation({
    mutationFn: (id: string) => apiVoid(`/api/events/${enc(eventId)}/rooms/${enc(id)}/deactivate`, { method: 'POST' }),
    onSuccess: settled,
  })
}
