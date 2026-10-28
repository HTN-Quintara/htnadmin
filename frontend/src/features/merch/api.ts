import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, isApiError } from '@/app/api'
import {
  buyerHitSchema,
  buyerHitsSchema,
  eventMerchSettingsSchema,
  merchDashboardSchema,
  merchItemSchema,
  merchSaleSchema,
  merchVariantSchema,
  type BuyerHit,
  type MerchSaleMethod,
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
