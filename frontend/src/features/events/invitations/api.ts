import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiVoid } from '@/app/api'
import {
  createInvitationsResultSchema,
  invitationDetailSchema,
  invitationPreviewSchema,
  invitationTemplateSchema,
  invitationsListSchema,
  type InvitationStatus,
} from './schemas'

const enc = encodeURIComponent

export const invitationsKeys = {
  all: (eventId: string) => ['invitations', eventId] as const,
  list: (eventId: string, status?: InvitationStatus) => ['invitations', eventId, 'list', status ?? 'All'] as const,
  template: (eventId: string) => ['invitations', eventId, 'template'] as const,
}

export function useInvitations(eventId: string, status?: InvitationStatus) {
  return useQuery({
    queryKey: invitationsKeys.list(eventId, status),
    queryFn: () =>
      api(`/api/events/${enc(eventId)}/invitations${status ? `?status=${status}` : ''}`, invitationsListSchema),
    enabled: !!eventId,
  })
}

export function useInvitationTemplate(eventId: string) {
  return useQuery({
    queryKey: invitationsKeys.template(eventId),
    queryFn: () => api(`/api/events/${enc(eventId)}/invitations/template`, invitationTemplateSchema),
    enabled: !!eventId,
  })
}

function useInvitationsSettled(eventId: string) {
  const qc = useQueryClient()
  return () => qc.invalidateQueries({ queryKey: invitationsKeys.all(eventId) })
}

export function useUpdateInvitationTemplateMutation(eventId: string) {
  const settled = useInvitationsSettled(eventId)
  return useMutation({
    mutationFn: (body: { subject: string; body: string }) =>
      api(`/api/events/${enc(eventId)}/invitations/template`, invitationTemplateSchema, { method: 'PUT', body }),
    onSuccess: settled,
  })
}

export function useCreateInvitationsMutation(eventId: string) {
  const settled = useInvitationsSettled(eventId)
  return useMutation({
    mutationFn: (orgIds: string[]) =>
      api(`/api/events/${enc(eventId)}/invitations`, createInvitationsResultSchema, { method: 'POST', body: { orgIds } }),
    onSuccess: settled,
  })
}

export function usePreviewInvitationMutation(eventId: string) {
  return useMutation({
    mutationFn: (invitationId: string) =>
      api(`/api/events/${enc(eventId)}/invitations/${enc(invitationId)}/preview`, invitationPreviewSchema, { method: 'POST' }),
  })
}

export function useSendInvitationMutation(eventId: string) {
  const settled = useInvitationsSettled(eventId)
  return useMutation({
    mutationFn: (invitationId: string) =>
      api(`/api/events/${enc(eventId)}/invitations/${enc(invitationId)}/send`, invitationDetailSchema, { method: 'POST' }),
    onSuccess: settled,
  })
}

export function useAdvanceInvitationStatusMutation(eventId: string) {
  const settled = useInvitationsSettled(eventId)
  return useMutation({
    mutationFn: ({ invitationId, status, note }: { invitationId: string; status: InvitationStatus; note?: string }) =>
      api(`/api/events/${enc(eventId)}/invitations/${enc(invitationId)}/status`, invitationDetailSchema, {
        method: 'POST',
        body: { status, note },
      }),
    onSuccess: settled,
  })
}

export function useDeleteInvitationMutation(eventId: string) {
  const settled = useInvitationsSettled(eventId)
  return useMutation({
    mutationFn: (invitationId: string) => apiVoid(`/api/events/${enc(eventId)}/invitations/${enc(invitationId)}`, { method: 'DELETE' }),
    onSuccess: settled,
  })
}
