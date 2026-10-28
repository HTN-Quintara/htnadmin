// API fixtures for the merch & POS slice (frame 20/21), shaped like MerchItemDto / MerchDashboardDto / BuyerHitDto.

export const merchVariant = (over: Record<string, unknown> = {}) => ({
  id: 'v1',
  itemId: 'i1',
  label: 'M',
  stockOnHand: 20,
  isActive: true,
  lowStock: false,
  ...over,
})

export const merchItem = (over: Record<string, unknown> = {}) => ({
  id: 'i1',
  eventId: 'e1',
  leftoverSourceEventId: null,
  name: 'HTN Con Tee',
  price: 450,
  swatchColor: '#B71C1C',
  isActive: true,
  variants: [
    merchVariant({ id: 'v1', itemId: 'i1', label: 'S', stockOnHand: 12 }),
    merchVariant({ id: 'v2', itemId: 'i1', label: 'M', stockOnHand: 3, lowStock: true }),
    merchVariant({ id: 'v3', itemId: 'i1', label: 'L', stockOnHand: 0, lowStock: true }),
  ],
  ...over,
})

export const totebagItem = (over: Record<string, unknown> = {}) => ({
  id: 'i2',
  eventId: 'e1',
  leftoverSourceEventId: null,
  name: 'Tote bag',
  price: 180,
  swatchColor: '#374151',
  isActive: true,
  variants: [merchVariant({ id: 'v4', itemId: 'i2', label: 'One size', stockOnHand: 30 })],
  ...over,
})

export const merchSaleLine = (over: Record<string, unknown> = {}) => ({
  id: 'l1',
  itemId: 'i1',
  itemName: 'HTN Con Tee',
  variantId: 'v1',
  variantLabel: 'S',
  qty: 1,
  unitPriceSnapshot: 450,
  lineTotal: 450,
  ...over,
})

export const merchSale = (over: Record<string, unknown> = {}) => ({
  id: 's1',
  eventId: 'e1',
  buyerPersonId: 'p2',
  buyerAttendeeId: null,
  buyerNameSnapshot: 'Mika Soriano',
  buyerLevelSnapshot: 'Goon',
  subtotal: 450,
  discountPct: 20,
  discountAmount: 90,
  total: 360,
  method: 'Cash',
  cashierId: 'u1',
  station: 'Booth 1',
  soldAt: '2026-10-28T03:00:00Z',
  lines: [merchSaleLine()],
  membershipFeeIncluded: false,
  membershipFeeAmount: null,
  ...over,
})

export const merchKpi = (over: Record<string, unknown> = {}) => ({
  orderCount: 1,
  itemsSold: 1,
  itemsInStock: 65,
  lowStockVariantCount: 2,
  totalSales: 360,
  discountsGiven: 90,
  ...over,
})

export const merchDashboard = (over: Record<string, unknown> = {}) => ({
  canManage: true,
  kpi: merchKpi(),
  items: [merchItem(), totebagItem()],
  sales: [merchSale()],
  ...over,
})

export const eventMerchSettings = (over: Record<string, unknown> = {}) => ({
  eventId: 'e1',
  goonPct: 20,
  apprenticePct: 15,
  memberPct: 10,
  boardPct: 0,
  ...over,
})

export const buyerHit = (over: Record<string, unknown> = {}) => ({
  kind: 'Person',
  personId: 'p2',
  attendeeId: null,
  name: 'Mika Soriano',
  level: 'Goon',
  email: 'mika@hackthenorth.ph',
  ...over,
})

export const attendeeBuyerHit = (over: Record<string, unknown> = {}) => ({
  kind: 'Attendee',
  personId: null,
  attendeeId: 'a1',
  name: 'Jo Attendee',
  level: 'Attendee',
  email: 'jo@example.com',
  ...over,
})
