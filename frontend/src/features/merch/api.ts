import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiVoid, isApiError } from '@/app/api'
import { committeeKeys } from '@/features/committees/api'
import { committeesPageSchema } from '@/features/committees/schemas'
import { useEventsQuery } from '@/features/events/api'
import {
  buyerHitSchema,
  buyerHitsSchema,
  eventMerchSettingsSchema,
  leftoverInventorySchema,
  merchDashboardSchema,
  merchItemSchema,
  merchSaleSchema,
  merchVariantSchema,
  popUpSchema,
  popUpsListSchema,
  type BuyerHit,
  type MerchSaleMethod,
  type PopUpDiscountMode,
} from './schemas'

const enc = encodeURIComponent

export const merchKeys = {
  all: ['merch'] as const,
  dashboard: (eventId: string) => ['merch', 'dashboard', eventId] as const,
  discounts: (eventId: string) => ['merch', 'discounts', eventId] as const,
  buyerSearch: (eventId: string, q: string) => ['merch', 'buyer-search', eventId, q] as const,
}

function useMerchSettled(eventId: string) {
  const qc = useQueryClient()
  return () => {
    void qc.invalidateQueries({ queryKey: merchKeys.dashboard(eventId) })
    void qc.invalidateQueries({ queryKey: merchKeys.discounts(eventId) })
  }
}

// ---- Dashboard ----

export function useMerchDashboard(eventId: string | undefined) {
  return useQuery({
    queryKey: merchKeys.dashboard(eventId ?? ''),
    queryFn: () => api(`/api/events/${enc(eventId ?? '')}/merch`, merchDashboardSchema),
    enabled: !!eventId,
  })
}

export function exportMerchSalesCsvUrl(eventId: string): string {
  return `/api/events/${enc(eventId)}/merch/sales/export`
}

// ---- Catalog ----

export type NewVariantBody = { label: string; stockOnHand: number }
export type NewItemBody = { name: string; price: number; swatchColor: string | null; variants: NewVariantBody[] }

/** Creates the item, then each variant in sequence (two endpoints, one form). */
export function useCreateItemMutation(eventId: string) {
  const settled = useMerchSettled(eventId)
  return useMutation({
    mutationFn: async (body: NewItemBody) => {
      const item = await api(`/api/events/${enc(eventId)}/merch/items`, merchItemSchema, {
        method: 'POST',
        body: { name: body.name, price: body.price, swatchColor: body.swatchColor },
      })
      for (const v of body.variants) {
        await api(`/api/events/${enc(eventId)}/merch/items/${enc(item.id)}/variants`, merchVariantSchema, {
          method: 'POST',
          body: { label: v.label, stockOnHand: v.stockOnHand },
        })
      }
      return item
    },
    onSuccess: settled,
  })
}

// ---- Discounts ----

/** Board-only endpoint on the server (`Merch.Manage`): only call once the caller can manage, not just sell. */
export function useMerchDiscounts(eventId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: merchKeys.discounts(eventId ?? ''),
    queryFn: () => api(`/api/events/${enc(eventId ?? '')}/merch/discounts`, eventMerchSettingsSchema),
    enabled: !!eventId && enabled,
  })
}

export type DiscountsBody = { goonPct: number; apprenticePct: number; memberPct: number; boardPct: number }

export function useUpdateDiscountsMutation(eventId: string) {
  const settled = useMerchSettled(eventId)
  return useMutation({
    mutationFn: (body: DiscountsBody) => api(`/api/events/${enc(eventId)}/merch/discounts`, eventMerchSettingsSchema, { method: 'PUT', body }),
    onSuccess: settled,
  })
}

/**
 * Same discounts endpoint, but for the POS totals preview: a committee lead can sell without `Merch.Manage`, so a
 * 403 here just means "no preview pct" (null), not a page error — the server still applies the right discount when
 * the sale posts.
 */
export function useMerchDiscountsPreview(eventId: string | undefined) {
  return useQuery({
    queryKey: merchKeys.discounts(eventId ?? ''),
    queryFn: async () => {
      try {
        return await api(`/api/events/${enc(eventId ?? '')}/merch/discounts`, eventMerchSettingsSchema)
      } catch (e) {
        if (isApiError(e, 403)) return null
        throw e
      }
    },
    enabled: !!eventId,
  })
}

// ---- POS ----

export function useBuyerSearch(eventId: string, q: string) {
  const term = q.trim()
  return useQuery({
    queryKey: merchKeys.buyerSearch(eventId, term),
    queryFn: () => api(`/api/events/${enc(eventId)}/pos/buyer-search?q=${enc(term)}`, buyerHitsSchema),
    enabled: !!eventId && term.length >= 3,
  })
}

export function useScanBuyerMutation(eventId: string) {
  return useMutation({
    mutationFn: (payload: string) => api(`/api/events/${enc(eventId)}/pos/scan`, buyerHitSchema, { method: 'POST', body: { payload } }),
  })
}

export type SaleLineBody = { variantId: string; qty: number }
export type NewSaleBody = {
  clientRequestId: string
  buyerPersonId: string | null
  buyerAttendeeId: string | null
  buyerName: string
  lines: SaleLineBody[]
  method: MerchSaleMethod
  station: string | null
  /** Phase 8B: the "Membership {year}" POS tile, for this sale's own buyer. */
  includesMembership: boolean
}

export function useSellMutation(eventId: string) {
  const settled = useMerchSettled(eventId)
  return useMutation({
    mutationFn: (body: NewSaleBody) => api(`/api/events/${enc(eventId)}/pos/sale`, merchSaleSchema, { method: 'POST', body }),
    onSuccess: settled,
  })
}

export type { BuyerHit }

// ---- Merch store: leftover inventory + move-to-store (Phase 8C) ----

export function useLeftoverInventory() {
  return useQuery({
    queryKey: ['merch', 'leftover-inventory'] as const,
    queryFn: () => api('/api/merch/leftover-inventory', leftoverInventorySchema),
  })
}

export function useMoveToStoreMutation(eventId: string) {
  const settled = useMerchSettled(eventId)
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (variantIds: string[]) => apiVoid(`/api/events/${enc(eventId)}/merch/move-to-store`, { method: 'POST', body: { variantIds } }),
    onSuccess: () => {
      settled()
      void qc.invalidateQueries({ queryKey: ['merch', 'leftover-inventory'] })
    },
  })
}

// ---- Pop-up stores (Phase 8C) ----

export const popUpKeys = {
  all: ['pop-ups'] as const,
  list: ['pop-ups', 'list'] as const,
  detail: (id: string) => ['pop-ups', 'detail', id] as const,
  buyerSearch: (id: string, q: string) => ['pop-ups', id, 'buyer-search', q] as const,
}

export function usePopUpsQuery() {
  return useQuery({
    queryKey: popUpKeys.list,
    queryFn: () => api('/api/pop-ups', popUpsListSchema),
  })
}

export function usePopUpQuery(id: string | undefined) {
  return useQuery({
    queryKey: popUpKeys.detail(id ?? ''),
    queryFn: () => api(`/api/pop-ups/${enc(id ?? '')}`, popUpSchema),
    enabled: !!id,
  })
}

function usePopUpsInvalidate() {
  const qc = useQueryClient()
  return (id?: string) => {
    void qc.invalidateQueries({ queryKey: popUpKeys.list })
    if (id) void qc.invalidateQueries({ queryKey: popUpKeys.detail(id) })
    void qc.invalidateQueries({ queryKey: ['merch', 'leftover-inventory'] })
  }
}

export type NewPopUpBody = {
  name: string
  venueDirectoryOrgId: string | null
  venueText: string | null
  startsAt: string
  endsAt: string
  feeAmount: number
  discountMode: PopUpDiscountMode
  goonPct: number | null
  apprenticePct: number | null
  memberPct: number | null
  boardPct: number | null
  ownerCommitteeId: string
  teamPersonIds: string[]
}

export function useCreatePopUpMutation() {
  const invalidate = usePopUpsInvalidate()
  return useMutation({
    mutationFn: (body: NewPopUpBody) => api('/api/pop-ups', popUpSchema, { method: 'POST', body }),
    onSuccess: () => invalidate(),
  })
}

export type AllocationLineBody = { variantId: string; qty: number }

export function useAllocatePopUpMutation(id: string) {
  const invalidate = usePopUpsInvalidate()
  return useMutation({
    mutationFn: (lines: AllocationLineBody[]) => api(`/api/pop-ups/${enc(id)}/allocations`, popUpSchema, { method: 'POST', body: { lines } }),
    onSuccess: () => invalidate(id),
  })
}

export function useOpenPopUpMutation(id: string) {
  const invalidate = usePopUpsInvalidate()
  return useMutation({
    mutationFn: () => api(`/api/pop-ups/${enc(id)}/open`, popUpSchema, { method: 'POST' }),
    onSuccess: () => invalidate(id),
  })
}

export function useClosePopUpMutation(id: string) {
  const invalidate = usePopUpsInvalidate()
  return useMutation({
    mutationFn: () => api(`/api/pop-ups/${enc(id)}/close`, popUpSchema, { method: 'POST' }),
    onSuccess: () => invalidate(id),
  })
}

export type CountLineBody = { variantId: string; qtyReturned: number }

export function useCountPopUpMutation(id: string) {
  const invalidate = usePopUpsInvalidate()
  return useMutation({
    mutationFn: (body: { returned: CountLineBody[]; cashCounted: number }) =>
      api(`/api/pop-ups/${enc(id)}/count`, popUpSchema, { method: 'POST', body }),
    onSuccess: () => invalidate(id),
  })
}

export function useWriteOffPopUpMutation(id: string) {
  const invalidate = usePopUpsInvalidate()
  return useMutation({
    mutationFn: (reason: string) => api(`/api/pop-ups/${enc(id)}/write-off`, popUpSchema, { method: 'POST', body: { reason } }),
    onSuccess: () => invalidate(id),
  })
}

// ---- Pop-up POS ----

export function usePopUpBuyerSearch(popUpId: string, q: string) {
  const term = q.trim()
  return useQuery({
    queryKey: popUpKeys.buyerSearch(popUpId, term),
    queryFn: () => api(`/api/pop-ups/${enc(popUpId)}/pos/buyer-search?q=${enc(term)}`, buyerHitsSchema),
    enabled: !!popUpId && term.length >= 3,
  })
}

export type NewPopUpSaleBody = {
  clientRequestId: string
  buyerPersonId: string | null
  buyerAttendeeId: string | null
  buyerName: string
  lines: SaleLineBody[]
  method: MerchSaleMethod
  station: string | null
}

export function useSellPopUpMutation(popUpId: string) {
  const invalidate = usePopUpsInvalidate()
  return useMutation({
    mutationFn: (body: NewPopUpSaleBody) =>
      api(`/api/pop-ups/${enc(popUpId)}/pos/sale`, merchSaleSchema, {
        method: 'POST',
        body: { ...body, buyerAttendeeId: null, includesMembership: false },
      }),
    onSuccess: () => invalidate(popUpId),
  })
}

// ---- Committees across events, for "Who can create pop-ups" (board-only) ----
// Pop-ups are owned by a per-event Committee row (`PopUpsAllowed`), not a global committee concept, so this reads
// every event's own committees endpoint and flattens the result — see MerchStorePage's report for why.

export type EligibleCommittee = {
  eventId: string
  eventName: string
  committeeId: string
  name: string
  popUpsAllowed: boolean
}

export function usePopUpEligibleCommittees() {
  const events = useEventsQuery('all')
  const eventList = events.data ?? []
  const committeeQueries = useQueries({
    queries: eventList.map((e) => ({
      queryKey: committeeKeys.event(e.id),
      queryFn: () => api(`/api/events/${enc(e.id)}/committees`, committeesPageSchema),
      enabled: events.isSuccess,
    })),
  })

  const isPending = events.isPending || (events.isSuccess && committeeQueries.some((q) => q.isPending))
  const isError = events.isError || committeeQueries.some((q) => q.isError)
  const refetch = () => {
    void events.refetch()
    for (const q of committeeQueries) void q.refetch()
  }

  const committees: EligibleCommittee[] = eventList.flatMap((e, i) => {
    const page = committeeQueries[i]?.data
    if (!page) return []
    return page.committees
      .filter((c) => c.isActive)
      .map((c) => ({ eventId: e.id, eventName: e.name, committeeId: c.id, name: c.name, popUpsAllowed: c.popUpsAllowed }))
  })

  return { committees, isPending, isError, refetch }
}
