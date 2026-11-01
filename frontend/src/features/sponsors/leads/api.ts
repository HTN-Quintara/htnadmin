import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/app/api'
import { confirmResultSchema, leadsBoardSchema, type LeadKind } from './schemas'

const enc = encodeURIComponent

export const leadsKeys = {
  all: (eventId: string) => ['leads', eventId] as const,
  board: (eventId: string, kind: LeadKind) => ['leads', eventId, kind] as const,
}

export function useLeadsBoardQuery(eventId: string, kind: LeadKind) {
  return useQuery({
    queryKey: leadsKeys.board(eventId, kind),
    queryFn: () => api(`/api/events/${enc(eventId)}/leads?kind=${kind}`, leadsBoardSchema),
    enabled: !!eventId,
  })
}

function useLeadsSettled(eventId: string) {
  const qc = useQueryClient()
  return () => qc.invalidateQueries({ queryKey: leadsKeys.all(eventId) })
}

export type LeadBody = Record<string, unknown>

export function useSaveLeadMutation(eventId: string) {
  const settled = useLeadsSettled(eventId)
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: LeadBody }) =>
      api(id ? `/api/events/${enc(eventId)}/leads/${enc(id)}` : `/api/events/${enc(eventId)}/leads`, leadsBoardSchema, {
        method: id ? 'PUT' : 'POST',
        body,
      }),
    onSuccess: settled,
  })
}

export function useLeadStatusMutation(eventId: string) {
  const settled = useLeadsSettled(eventId)
  return useMutation({
    mutationFn: ({ id, status, reason }: { id: string; status: string; reason?: string }) =>
      api(`/api/events/${enc(eventId)}/leads/${enc(id)}/status`, leadsBoardSchema, { method: 'POST', body: { status, reason } }),
    onSuccess: settled,
  })
}

export function useConfirmLeadMutation(eventId: string) {
  const settled = useLeadsSettled(eventId)
  return useMutation({
    mutationFn: (id: string) => api(`/api/events/${enc(eventId)}/leads/${enc(id)}/confirm`, confirmResultSchema, { method: 'POST' }),
    onSuccess: settled,
  })
}
