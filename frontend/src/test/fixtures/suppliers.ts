// API fixtures for the suppliers slice (frame 18b/18c), shaped like SupplierDto / SupplierDetailDto / EventSupplierDto.

/**
 * `boardOnly` is present for a board caller and *entirely absent* (not `boardOnly: null`) for anyone else — the
 * real backend's `JsonIgnoreCondition.WhenWritingNull`. `asBoard` controls which shape this fixture returns, so a
 * test can assert the key itself is missing from the rendered output, not merely blank.
 */
export function supplier(asBoard: boolean, over: Record<string, unknown> = {}) {
  const base: Record<string, unknown> = {
    id: 's1',
    category: 'Venue',
    name: 'Grand Hall Events Place',
    contactName: 'Mia Torres',
    contactEmail: 'mia@grandhall.ph',
    contactPhone: '+639170001111',
    issuesOr: true,
    notes: 'Preferred venue for the main conference day.',
    logoFileId: null,
    isActive: true,
  }
  if (asBoard) base.boardOnly = { tin: '123-456-789-000', bankDetails: 'BDO 0012-3456-7890' }
  return { ...base, ...over }
}

export function cateringSupplier(asBoard: boolean, over: Record<string, unknown> = {}) {
  return supplier(asBoard, {
    id: 's2',
    category: 'Catering',
    name: 'Lami Catering Co.',
    contactName: 'Jun Dela Cruz',
    contactEmail: 'jun@lamicatering.ph',
    contactPhone: null,
    issuesOr: false,
    notes: null,
    ...(asBoard ? { boardOnly: { tin: null, bankDetails: null } } : {}),
    ...over,
  })
}

export function suppliersList(asBoard: boolean) {
  return [supplier(asBoard), cateringSupplier(asBoard)]
}

export function supplierDocument(over: Record<string, unknown> = {}) {
  return {
    id: 'd1',
    supplierId: 's1',
    kind: 'Bir2303',
    fileId: 'f1',
    uploadedAt: '2026-09-01T00:00:00Z',
    uploadedById: 'u1',
    isVoided: false,
    voidReason: null,
    ...over,
  }
}

export function eventSupplierHire(asBoard: boolean, over: Record<string, unknown> = {}) {
  const base: Record<string, unknown> = {
    id: 'es1',
    eventId: 'e1',
    supplierId: 's1',
    supplierName: 'Grand Hall Events Place',
    categoryId: 'cat1',
    categoryName: 'Venue',
    ownerCommitteeId: 'com1',
    ownerCommitteeName: 'Logistics',
    status: 'Booked',
    note: null,
    isVoided: false,
    voidReason: null,
    averageStars: null,
    ratingCount: 0,
    canRate: true,
    alreadyRatedByMe: false,
  }
  if (asBoard) base.money = { contractAmount: 150000, paidAmount: 50000, nextPaymentAmount: 50000, nextPaymentDue: '2026-11-01' }
  return { ...base, ...over }
}

export function supplierDetail(asBoard: boolean, over: Record<string, unknown> = {}) {
  return {
    supplier: supplier(asBoard),
    documents: [supplierDocument()],
    hires: [eventSupplierHire(asBoard)],
    ...over,
  }
}

export function toRateHire(over: Record<string, unknown> = {}) {
  return eventSupplierHire(true, {
    id: 'es2',
    status: 'FullyPaid',
    alreadyRatedByMe: false,
    ...over,
  })
}
