// API fixtures for the merch store & pop-up slice (frame 21b, Phase 8C), shaped like PopUpDto / LeftoverVariantDto.

export const popUpAllocation = (over: Record<string, unknown> = {}) => ({
  variantId: 'v1',
  itemName: 'HTN Con Tee',
  variantLabel: 'S',
  qtyBrought: 10,
  qtySold: 3,
  qtyReturned: null,
  qtyMissing: null,
  ...over,
})

export const popUp = (over: Record<string, unknown> = {}) => ({
  id: 'pu1',
  name: 'HTN Pop-up · SM Megamall',
  venueDirectoryOrgId: null,
  venueOrgName: null,
  venueText: 'SM Megamall atrium',
  startsAt: '2026-11-10T02:00:00Z',
  endsAt: '2026-11-10T10:00:00Z',
  feeAmount: 500,
  discountMode: 'SameAsDefault',
  goonPct: null,
  apprenticePct: null,
  memberPct: null,
  boardPct: null,
  ownerCommitteeId: 'c1',
  ownerCommitteeName: 'Logistics',
  status: 'Open',
  team: [{ personId: 'p7', name: 'Dana Ramos' }],
  allocations: [popUpAllocation()],
  totalSales: 1350,
  discountsGiven: 0,
  cashCounted: null,
  missingValue: null,
  isWrittenOff: false,
  writtenOffAt: null,
  canManage: true,
  canSell: true,
  ...over,
})

export const plannedPopUp = (over: Record<string, unknown> = {}) =>
  popUp({
    id: 'pu2',
    name: 'HTN Pop-up · Ateneo bazaar',
    status: 'Planned',
    allocations: [],
    totalSales: null,
    discountsGiven: null,
    cashCounted: null,
    missingValue: null,
    ...over,
  })

export const closedMissingPopUp = (over: Record<string, unknown> = {}) =>
  popUp({
    id: 'pu3',
    name: 'HTN Pop-up · UP Town Center',
    status: 'ClosedMissing',
    allocations: [popUpAllocation({ qtyBrought: 10, qtySold: 7, qtyReturned: 2, qtyMissing: 1 })],
    totalSales: 3150,
    discountsGiven: 0,
    cashCounted: 3000,
    missingValue: 450,
    isWrittenOff: false,
    ...over,
  })

export const leftoverVariant = (over: Record<string, unknown> = {}) => ({
  itemId: 'i1',
  itemName: 'HTN Con Tee',
  variantId: 'v1',
  variantLabel: 'S',
  price: 450,
  fromEventId: 'e1',
  fromEventName: 'HTN Con 2026',
  stockOnHand: 20,
  reserved: 10,
  available: 10,
  ...over,
})
