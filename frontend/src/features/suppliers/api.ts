import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiRequest, apiVoid } from '@/app/api'
import {
  eventSuppliersListSchema,
  supplierDetailSchema,
  suppliersListSchema,
  supplierDocumentSchema,
  supplierRatingSchema,
  supplierSchema,
  eventSupplierSchema,
  type HireAgain,
  type SupplierCategory,
  type SupplierDocumentKind,
  type SupplierHireStatus,
} from './schemas'

const enc = encodeURIComponent

export const suppliersKeys = {
  all: ['suppliers'] as const,
  list: (category?: SupplierCategory) => ['suppliers', 'list', category ?? null] as const,
  detail: (id: string) => ['suppliers', 'detail', id] as const,
  event: (eventId: string) => ['suppliers', 'event', eventId] as const,
  toRate: (eventId: string) => ['suppliers', 'to-rate', eventId] as const,
}

// ---- Global catalog ----

export function useSuppliers(category?: SupplierCategory) {
  return useQuery({
    queryKey: suppliersKeys.list(category),
    queryFn: () => api(category ? `/api/suppliers?category=${enc(category)}` : '/api/suppliers', suppliersListSchema),
  })
}

export function useSupplier(id: string | null) {
  return useQuery({
    queryKey: suppliersKeys.detail(id ?? ''),
    queryFn: () => api(`/api/suppliers/${enc(id ?? '')}`, supplierDetailSchema),
    enabled: !!id,
  })
}

function useSuppliersSettled() {
  const qc = useQueryClient()
  return () => qc.invalidateQueries({ queryKey: suppliersKeys.all })
}

export type NewSupplierBody = {
  category: SupplierCategory
  name: string
  contactName: string | null
  contactEmail: string | null
  contactPhone: string | null
  issuesOr: boolean
  tin: string | null
  bankDetails: string | null
  notes: string | null
}

export type UpdateSupplierBody = {
  contactName: string | null
  contactEmail: string | null
  contactPhone: string | null
  issuesOr: boolean
  tin: string | null
  bankDetails: string | null
  notes: string | null
}

export function useCreateSupplierMutation() {
  const settled = useSuppliersSettled()
  return useMutation({
    mutationFn: (body: NewSupplierBody) => api('/api/suppliers', supplierSchema, { method: 'POST', body }),
    onSuccess: settled,
  })
}

export function useUpdateSupplierMutation() {
  const settled = useSuppliersSettled()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateSupplierBody }) =>
      api(`/api/suppliers/${enc(id)}`, supplierSchema, { method: 'PATCH', body }),
    onSuccess: settled,
  })
}

export function useDeactivateSupplierMutation() {
  const settled = useSuppliersSettled()
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      apiVoid(`/api/suppliers/${enc(id)}/deactivate`, { method: 'POST', body: { reason } }),
    onSuccess: settled,
  })
}

export function useUploadSupplierLogoMutation() {
  const settled = useSuppliersSettled()
  return useMutation({
    mutationFn: async ({ id, file }: { id: string; file: File }) => {
      const form = new FormData()
      form.append('file', file)
      await apiRequest(`/api/suppliers/${enc(id)}/logo`, { method: 'PUT', body: form })
    },
    onSuccess: settled,
  })
}

// ---- Documents ----

export function useUploadSupplierDocumentMutation() {
  const settled = useSuppliersSettled()
  return useMutation({
    mutationFn: async ({ id, kind, file }: { id: string; kind: SupplierDocumentKind; file: File }) => {
      const form = new FormData()
      form.append('file', file)
      form.append('kind', kind)
      return api(`/api/suppliers/${enc(id)}/documents`, supplierDocumentSchema, { method: 'POST', body: form })
    },
    onSuccess: settled,
  })
}

export function useVoidSupplierDocumentMutation() {
  const settled = useSuppliersSettled()
  return useMutation({
    mutationFn: ({ id, docId, reason }: { id: string; docId: string; reason: string }) =>
      api(`/api/suppliers/${enc(id)}/documents/${enc(docId)}/void`, supplierDocumentSchema, { method: 'POST', body: { reason } }),
    onSuccess: settled,
  })
}

export function supplierDocumentDownloadUrl(id: string, docId: string): string {
  return `/api/suppliers/${enc(id)}/documents/${enc(docId)}/download`
}

// ---- Per-event hires ----

export function useEventSuppliers(eventId: string | undefined) {
  return useQuery({
    queryKey: suppliersKeys.event(eventId ?? ''),
    queryFn: () => api(`/api/events/${enc(eventId ?? '')}/suppliers`, eventSuppliersListSchema),
    enabled: !!eventId,
  })
}

export function useSuppliersToRate(eventId: string | undefined) {
  return useQuery({
    queryKey: suppliersKeys.toRate(eventId ?? ''),
    queryFn: () => api(`/api/events/${enc(eventId ?? '')}/suppliers/to-rate`, eventSuppliersListSchema),
    enabled: !!eventId,
  })
}

function useEventSuppliersSettled(eventId: string) {
  const qc = useQueryClient()
  return () => {
    qc.invalidateQueries({ queryKey: suppliersKeys.event(eventId) })
    qc.invalidateQueries({ queryKey: suppliersKeys.toRate(eventId) })
    qc.invalidateQueries({ queryKey: suppliersKeys.all })
  }
}

export type NewHireBody = { supplierId: string; categoryId: string; ownerCommitteeId: string; note: string | null }

export function useCreateHireMutation(eventId: string) {
  const settled = useEventSuppliersSettled(eventId)
  return useMutation({
    mutationFn: (body: NewHireBody) => api(`/api/events/${enc(eventId)}/suppliers`, eventSupplierSchema, { method: 'POST', body }),
    onSuccess: settled,
  })
}

export type UpdateHireBody = {
  contractAmount: number | null
  paidAmount: number | null
  nextPaymentAmount: number | null
  nextPaymentDue: string | null
  note: string | null
  status?: SupplierHireStatus
}

export function useUpdateHireMutation(eventId: string) {
  const settled = useEventSuppliersSettled(eventId)
  return useMutation({
    mutationFn: ({ hireId, body }: { hireId: string; body: UpdateHireBody }) =>
      api(`/api/events/${enc(eventId)}/suppliers/${enc(hireId)}`, eventSupplierSchema, { method: 'PATCH', body }),
    onSuccess: settled,
  })
}

export function useVoidHireMutation(eventId: string) {
  const settled = useEventSuppliersSettled(eventId)
  return useMutation({
    mutationFn: ({ hireId, reason }: { hireId: string; reason: string }) =>
      api(`/api/events/${enc(eventId)}/suppliers/${enc(hireId)}/void`, eventSupplierSchema, { method: 'POST', body: { reason } }),
    onSuccess: settled,
  })
}

export function useDeleteHireMutation(eventId: string) {
  const settled = useEventSuppliersSettled(eventId)
  return useMutation({
    mutationFn: (hireId: string) => apiVoid(`/api/events/${enc(eventId)}/suppliers/${enc(hireId)}`, { method: 'DELETE' }),
    onSuccess: settled,
  })
}

export type RatingBody = { stars: number; tags: string[]; hireAgain: HireAgain; note: string | null }

export function useSubmitRatingMutation(eventId: string) {
  const settled = useEventSuppliersSettled(eventId)
  return useMutation({
    mutationFn: ({ hireId, body }: { hireId: string; body: RatingBody }) =>
      api(`/api/events/${enc(eventId)}/suppliers/${enc(hireId)}/ratings`, supplierRatingSchema, { method: 'POST', body }),
    onSuccess: settled,
  })
}
