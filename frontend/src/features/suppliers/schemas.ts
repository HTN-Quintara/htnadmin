import { z } from 'zod'

export const supplierCategories = ['Venue', 'Catering', 'Printing', 'TechAV', 'Merch', 'Transport'] as const
export type SupplierCategory = (typeof supplierCategories)[number]

export const supplierHireStatuses = ['Quotation', 'Booked', 'DepositPaid', 'FullyPaid'] as const
export type SupplierHireStatus = (typeof supplierHireStatuses)[number]

export const supplierDocumentKinds = ['Bir2303', 'BusinessPermit', 'Other'] as const
export type SupplierDocumentKind = (typeof supplierDocumentKinds)[number]

export const hireAgainValues = ['Yes', 'Maybe', 'No'] as const
export type HireAgain = (typeof hireAgainValues)[number]

export const supplierRatingTags = ['OnTime', 'GoodQuality', 'EasyToReach', 'FairPrice', 'Late', 'BelowExpectations'] as const
export type SupplierRatingTag = (typeof supplierRatingTags)[number]

/**
 * Board-only money (TIN + bank). Optional and, on the wire, entirely absent (not null) for a caller without
 * Suppliers.Manage — the backend's `JsonIgnoreCondition.WhenWritingNull` on `SupplierDto.BoardOnly`. The MSW
 * fixtures must omit the key the same way, not set it to null, so this schema catches a regression either way.
 */
export const supplierBoardOnlySchema = z.object({
  tin: z.string().nullable(),
  bankDetails: z.string().nullable(),
})
export type SupplierBoardOnly = z.infer<typeof supplierBoardOnlySchema>

export const supplierSchema = z.object({
  id: z.string(),
  category: z.enum(supplierCategories),
  name: z.string(),
  contactName: z.string().nullable(),
  contactEmail: z.string().nullable(),
  contactPhone: z.string().nullable(),
  issuesOr: z.boolean(),
  notes: z.string().nullable(),
  logoFileId: z.string().nullable(),
  isActive: z.boolean(),
  boardOnly: supplierBoardOnlySchema.optional(),
})
export type Supplier = z.infer<typeof supplierSchema>

export const supplierDocumentSchema = z.object({
  id: z.string(),
  supplierId: z.string(),
  kind: z.enum(supplierDocumentKinds),
  fileId: z.string(),
  uploadedAt: z.string(),
  uploadedById: z.string().nullable(),
  isVoided: z.boolean(),
  voidReason: z.string().nullable(),
})
export type SupplierDocument = z.infer<typeof supplierDocumentSchema>

/** Same "wrapped and absent" shape as `SupplierBoardOnly`, for a hire's money fields. */
export const eventSupplierMoneySchema = z.object({
  contractAmount: z.number().nullable(),
  paidAmount: z.number().nullable(),
  nextPaymentAmount: z.number().nullable(),
  nextPaymentDue: z.string().nullable(),
})
export type EventSupplierMoney = z.infer<typeof eventSupplierMoneySchema>

export const eventSupplierSchema = z.object({
  id: z.string(),
  eventId: z.string(),
  supplierId: z.string(),
  supplierName: z.string(),
  categoryId: z.string(),
  categoryName: z.string(),
  ownerCommitteeId: z.string(),
  ownerCommitteeName: z.string(),
  status: z.enum(supplierHireStatuses),
  note: z.string().nullable(),
  isVoided: z.boolean(),
  voidReason: z.string().nullable(),
  money: eventSupplierMoneySchema.optional(),
  averageStars: z.number().nullable(),
  ratingCount: z.number(),
  canRate: z.boolean(),
  alreadyRatedByMe: z.boolean(),
})
export type EventSupplier = z.infer<typeof eventSupplierSchema>

export const supplierDetailSchema = z.object({
  supplier: supplierSchema,
  documents: z.array(supplierDocumentSchema),
  hires: z.array(eventSupplierSchema),
})
export type SupplierDetail = z.infer<typeof supplierDetailSchema>

export const supplierRatingSchema = z.object({
  id: z.string(),
  eventSupplierId: z.string(),
  raterId: z.string(),
  stars: z.number(),
  tags: z.array(z.string()),
  hireAgain: z.enum(hireAgainValues),
  note: z.string().nullable(),
  createdAt: z.string(),
})
export type SupplierRating = z.infer<typeof supplierRatingSchema>

export const suppliersListSchema = z.array(supplierSchema)
export const eventSuppliersListSchema = z.array(eventSupplierSchema)

// ---- Form schemas ----

export const supplierFormSchema = z.object({
  category: z.enum(supplierCategories),
  name: z.string().trim().min(1, 'Give a name.').max(160),
  contactName: z.string().trim().max(120).optional().or(z.literal('')),
  contactEmail: z.string().trim().max(254).optional().or(z.literal('')),
  contactPhone: z.string().trim().max(20).optional().or(z.literal('')),
  issuesOr: z.boolean(),
  tin: z.string().trim().max(40).optional().or(z.literal('')),
  bankDetails: z.string().trim().max(500).optional().or(z.literal('')),
  notes: z.string().trim().max(2000).optional().or(z.literal('')),
})
export type SupplierFormValues = z.infer<typeof supplierFormSchema>

export const hireFormSchema = z.object({
  supplierId: z.string().min(1, 'Pick a supplier.'),
  categoryId: z.string().min(1, 'Pick a budget category.'),
  ownerCommitteeId: z.string().min(1, 'Pick a committee.'),
  note: z.string().trim().max(500).optional().or(z.literal('')),
})
export type HireFormValues = z.infer<typeof hireFormSchema>

export const hireMoneyFormSchema = z.object({
  contractAmount: z.string().trim().max(20).optional().or(z.literal('')),
  paidAmount: z.string().trim().max(20).optional().or(z.literal('')),
  nextPaymentAmount: z.string().trim().max(20).optional().or(z.literal('')),
  nextPaymentDue: z.string().trim().optional().or(z.literal('')),
  note: z.string().trim().max(500).optional().or(z.literal('')),
})
export type HireMoneyFormValues = z.infer<typeof hireMoneyFormSchema>

export const ratingFormSchema = z.object({
  stars: z.number().int().min(1, 'Pick a star rating.').max(5),
  tags: z.array(z.enum(supplierRatingTags)),
  hireAgain: z.enum(hireAgainValues, { message: 'Pick an answer.' }),
  note: z.string().trim().max(500).optional().or(z.literal('')),
})
export type RatingFormValues = z.infer<typeof ratingFormSchema>
