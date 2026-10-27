import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiRequest, apiVoid } from '@/app/api'
import {
  directoryOrgDetailSchema,
  directoryOrgsListSchema,
  type DirectoryOrgKind,
} from './schemas'

const enc = encodeURIComponent

export const directoryKeys = {
  all: ['directory'] as const,
  list: (filters: DirectoryFilters) => ['directory', 'list', filters] as const,
  detail: (id: string) => ['directory', 'detail', id] as const,
}

export type DirectoryFilters = { kind?: DirectoryOrgKind; needsCheck?: boolean }

function listPath(filters: DirectoryFilters): string {
  const params = new URLSearchParams()
  if (filters.kind) params.set('kind', filters.kind)
  if (filters.needsCheck !== undefined) params.set('needsCheck', String(filters.needsCheck))
  const qs = params.toString()
  return qs ? `/api/directory?${qs}` : '/api/directory'
}

export function useDirectoryOrgs(filters: DirectoryFilters = {}) {
  return useQuery({
    queryKey: directoryKeys.list(filters),
    queryFn: () => api(listPath(filters), directoryOrgsListSchema),
  })
}

export function useDirectoryOrg(id: string | null) {
  return useQuery({
    queryKey: directoryKeys.detail(id ?? ''),
    queryFn: () => api(`/api/directory/${enc(id ?? '')}`, directoryOrgDetailSchema),
    enabled: !!id,
  })
}

/** One invalidation for the whole area: any write moves the list and whichever detail is open. */
function useDirectorySettled() {
  const qc = useQueryClient()
  return () => qc.invalidateQueries({ queryKey: directoryKeys.all })
}

export type NewOrgBody = {
  kind: DirectoryOrgKind
  name: string
  addresseeSalutation: string | null
  websiteUrl: string | null
  notes: string | null
}

export type UpdateOrgBody = {
  addresseeSalutation: string | null
  websiteUrl: string | null
  notes: string | null
}

export function useCreateOrgMutation() {
  const settled = useDirectorySettled()
  return useMutation({
    mutationFn: (body: NewOrgBody) => api('/api/directory', directoryOrgDetailSchema, { method: 'POST', body }),
    onSuccess: settled,
  })
}

export function useUpdateOrgMutation() {
  const settled = useDirectorySettled()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateOrgBody }) =>
      api(`/api/directory/${enc(id)}`, directoryOrgDetailSchema, { method: 'PATCH', body }),
    onSuccess: settled,
  })
}

export function useDeactivateOrgMutation() {
  const settled = useDirectorySettled()
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      apiVoid(`/api/directory/${enc(id)}/deactivate`, { method: 'POST', body: { reason } }),
    onSuccess: settled,
  })
}

export function useUploadOrgLogoMutation() {
  const settled = useDirectorySettled()
  return useMutation({
    mutationFn: async ({ id, file }: { id: string; file: File }) => {
      const form = new FormData()
      form.append('file', file)
      await apiRequest(`/api/directory/${enc(id)}/logo`, { method: 'PUT', body: form })
    },
    onSuccess: settled,
  })
}

export type ContactBody = {
  position: string
  holderName: string
  email: string
  phone: string | null
  isPrimary: boolean
  ccEmails: string | null
}

export function useCreateContactMutation() {
  const settled = useDirectorySettled()
  return useMutation({
    mutationFn: ({ orgId, body }: { orgId: string; body: ContactBody }) =>
      api(`/api/directory/${enc(orgId)}/contacts`, directoryOrgDetailSchema, { method: 'POST', body }),
    onSuccess: settled,
  })
}

export function useUpdateContactMutation() {
  const settled = useDirectorySettled()
  return useMutation({
    mutationFn: ({ orgId, contactId, body }: { orgId: string; contactId: string; body: ContactBody }) =>
      api(`/api/directory/${enc(orgId)}/contacts/${enc(contactId)}`, directoryOrgDetailSchema, { method: 'PATCH', body }),
    onSuccess: settled,
  })
}

export function useStillCorrectMutation() {
  const settled = useDirectorySettled()
  return useMutation({
    mutationFn: ({ orgId, contactId }: { orgId: string; contactId: string }) =>
      api(`/api/directory/${enc(orgId)}/contacts/${enc(contactId)}/still-correct`, directoryOrgDetailSchema, { method: 'POST' }),
    onSuccess: settled,
  })
}

export type HeadChangedBody = { position: string; holderName: string; email: string; phone: string | null }

export function useHeadChangedMutation() {
  const settled = useDirectorySettled()
  return useMutation({
    mutationFn: ({ orgId, contactId, body }: { orgId: string; contactId: string; body: HeadChangedBody }) =>
      api(`/api/directory/${enc(orgId)}/contacts/${enc(contactId)}/head-changed`, directoryOrgDetailSchema, { method: 'POST', body }),
    onSuccess: settled,
  })
}

export function useVoidContactMutation() {
  const settled = useDirectorySettled()
  return useMutation({
    mutationFn: ({ orgId, contactId, reason }: { orgId: string; contactId: string; reason: string }) =>
      api(`/api/directory/${enc(orgId)}/contacts/${enc(contactId)}/void`, directoryOrgDetailSchema, { method: 'POST', body: { reason } }),
    onSuccess: settled,
  })
}
