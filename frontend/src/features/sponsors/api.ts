import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiRequest, apiVoid } from '@/app/api'
import {
  groupLinkSchema,
  partnersPageSchema,
  sponsorDetailSchema,
  sponsorsPageSchema,
  type CountBasis,
} from './schemas'

const enc = encodeURIComponent

export const sponsorsKeys = {
  all: ['sponsors'] as const,
  page: (eventId: string) => ['sponsors', eventId, 'page'] as const,
  detail: (eventId: string, sid: string) => ['sponsors', eventId, 'detail', sid] as const,
  partners: (eventId: string) => ['sponsors', eventId, 'partners'] as const,
}

export function useSponsorsPageQuery(eventId: string) {
  return useQuery({
    queryKey: sponsorsKeys.page(eventId),
    queryFn: () => api(`/api/events/${enc(eventId)}/sponsors`, sponsorsPageSchema),
    enabled: !!eventId,
  })
}

export function useSponsorDetailQuery(eventId: string, sid: string | null) {
  return useQuery({
    queryKey: sponsorsKeys.detail(eventId, sid ?? ''),
    queryFn: () => api(`/api/events/${enc(eventId)}/sponsors/${enc(sid ?? '')}`, sponsorDetailSchema),
    enabled: !!eventId && !!sid,
  })
}

export function usePartnersQuery(eventId: string) {
  return useQuery({
    queryKey: sponsorsKeys.partners(eventId),
    queryFn: () => api(`/api/events/${enc(eventId)}/partners`, partnersPageSchema),
    enabled: !!eventId,
  })
}

/** One invalidation for the whole area: any write moves the page, a detail and the partner counts at once. */
export function useSponsorsSettled(eventId: string) {
  const qc = useQueryClient()
  return () => qc.invalidateQueries({ queryKey: ['sponsors', eventId] })
}

export type SponsorBody = Record<string, unknown>

export function useSaveSponsorMutation(eventId: string) {
  const settled = useSponsorsSettled(eventId)
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SponsorBody }) =>
      api(
        id ? `/api/events/${enc(eventId)}/sponsors/${enc(id)}` : `/api/events/${enc(eventId)}/sponsors`,
        sponsorDetailSchema,
        { method: id ? 'PUT' : 'POST', body },
      ),
    onSuccess: settled,
  })
}

export function useSponsorActionMutation(eventId: string) {
  const settled = useSponsorsSettled(eventId)
  return useMutation({
    mutationFn: ({ sid, action, body }: { sid: string; action: string; body?: SponsorBody }) =>
      apiRequest(`/api/events/${enc(eventId)}/sponsors/${enc(sid)}/${action}`, { method: 'POST', body }),
    onSuccess: settled,
  })
}

export function useCashReceivedMutation(eventId: string) {
  const settled = useSponsorsSettled(eventId)
  return useMutation({
    mutationFn: ({ sid, body }: { sid: string; body: SponsorBody }) =>
      api(`/api/events/${enc(eventId)}/sponsors/${enc(sid)}/cash-received`, sponsorDetailSchema, { method: 'POST', body }),
    onSuccess: settled,
  })
}

export function useUploadSponsorLogoMutation(eventId: string) {
  const settled = useSponsorsSettled(eventId)
  return useMutation({
    mutationFn: async ({ sid, file }: { sid: string; file: File }) => {
      const form = new FormData()
      form.append('file', file)
      await apiRequest(`/api/events/${enc(eventId)}/sponsors/${enc(sid)}/logo`, { method: 'PUT', body: form })
    },
    onSuccess: settled,
  })
}

export function useSaveTierMutation(eventId: string) {
  const settled = useSponsorsSettled(eventId)
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: SponsorBody }) =>
      api(
        id ? `/api/events/${enc(eventId)}/sponsor-tiers/${enc(id)}` : `/api/events/${enc(eventId)}/sponsor-tiers`,
        sponsorsPageSchema,
        { method: id ? 'PUT' : 'POST', body },
      ),
    onSuccess: settled,
  })
}

export function useDeactivateTierMutation(eventId: string) {
  const settled = useSponsorsSettled(eventId)
  return useMutation({
    mutationFn: (tid: string) => apiVoid(`/api/events/${enc(eventId)}/sponsor-tiers/${enc(tid)}/deactivate`, { method: 'POST' }),
    onSuccess: settled,
  })
}

// ---- Partners ----

export function useSavePartnerSettingsMutation(eventId: string) {
  const settled = useSponsorsSettled(eventId)
  return useMutation({
    mutationFn: (body: { threshold: number; countBasis: CountBasis; groupLinkEnabled: boolean }) =>
      api(`/api/events/${enc(eventId)}/partners/settings`, partnersPageSchema, { method: 'PUT', body }),
    onSuccess: settled,
  })
}

export function useTagPartnerMutation(eventId: string) {
  const settled = useSponsorsSettled(eventId)
  return useMutation({
    mutationFn: (body: { name: string; orgType?: string }) =>
      api(`/api/events/${enc(eventId)}/partners/tag`, partnersPageSchema, { method: 'POST', body }),
    onSuccess: settled,
  })
}

export function usePartnerActionMutation(eventId: string) {
  const settled = useSponsorsSettled(eventId)
  return useMutation({
    mutationFn: ({ pid, action, body }: { pid: string; action: string; body?: SponsorBody }) =>
      apiRequest(`/api/events/${enc(eventId)}/partners/${enc(pid)}/${action}`, { method: 'POST', body }),
    onSuccess: settled,
  })
}

export function useCreateGroupLinkMutation(eventId: string) {
  const settled = useSponsorsSettled(eventId)
  return useMutation({
    mutationFn: (pid: string) =>
      api(`/api/events/${enc(eventId)}/partners/${enc(pid)}/group-link`, groupLinkSchema, { method: 'POST' }),
    onSuccess: settled,
  })
}
