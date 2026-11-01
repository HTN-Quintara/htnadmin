import { z } from 'zod'
import { parseAmount, parseSignedAmount, AMOUNT_HINT, SIGNED_AMOUNT_HINT } from '@/lib/money'

// ---- Shared enums (mirrors Htn.Domain.Finance) -----------------------------------------

export const fundKindSchema = z.enum(['Organization', 'Event'])
export const directionSchema = z.enum(['In', 'Out'])
export const ledgerStatusSchema = z.enum(['Pending', 'Posted', 'Voided'])
export const budgetLineKindSchema = z.enum(['Income', 'Expense'])
export const requestStatusSchema = z.enum(['Pending', 'Approved', 'Rejected', 'Paid', 'Cancelled'])
export const ledgerSourceSchema = z.enum([
  'Manual',
  'TicketPayment',
  'Contra',
  'Transfer',
  'SurplusSweep',
  'ExpenseRequest',
  'SponsorPayment',
  'SupplierPayment',
  'MerchSale',
  'MembershipPayment',
  'PopUpWriteOff',
])

export type LedgerDirection = z.infer<typeof directionSchema>
export type LedgerStatus = z.infer<typeof ledgerStatusSchema>
export type LedgerSource = z.infer<typeof ledgerSourceSchema>
export type BudgetLineKind = z.infer<typeof budgetLineKindSchema>
export type RequestStatus = z.infer<typeof requestStatusSchema>

// ---- Responses -------------------------------------------------------------------------

export const fundSchema = z.object({
  id: z.string(),
  kind: fundKindSchema,
  eventId: z.string().nullable(),
  name: z.string(),
  balance: z.number(),
  isClosed: z.boolean(),
  isSwept: z.boolean(),
  sweptAt: z.string().nullable(),
  canSweep: z.boolean(),
})
export type Fund = z.infer<typeof fundSchema>

export const categorySchema = z.object({
  id: z.string(),
  code: z.string(),
  name: z.string(),
  kind: budgetLineKindSchema,
  isSystem: z.boolean(),
  isActive: z.boolean(),
})
export type LedgerCategory = z.infer<typeof categorySchema>

export const ledgerEntrySchema = z.object({
  id: z.string(),
  fundId: z.string(),
  fundName: z.string(),
  paymentAccountId: z.string().nullable(),
  accountLabel: z.string().nullable(),
  categoryId: z.string(),
  categoryName: z.string(),
  entryDate: z.string(),
  direction: directionSchema,
  amount: z.number(),
  signedAmount: z.number(),
  reference: z.string().nullable(),
  description: z.string(),
  hasReceipt: z.boolean(),
  status: ledgerStatusSchema,
  source: ledgerSourceSchema,
  transferGroupId: z.string().nullable(),
  contraOfEntryId: z.string().nullable(),
  isInClosedMonth: z.boolean(),
  enteredBy: z.string().nullable(),
  enteredAt: z.string(),
  approvedBy: z.string().nullable(),
  approvedAt: z.string().nullable(),
  voidedBy: z.string().nullable(),
  voidedAt: z.string().nullable(),
  voidReason: z.string().nullable(),
  /** Per-record, because "can I void this?" depends on the row: a closed month freezes it. */
  canApprove: z.boolean(),
  canVoid: z.boolean(),
})
export type LedgerEntry = z.infer<typeof ledgerEntrySchema>

export const ledgerTotalsSchema = z.object({
  moneyIn: z.number(),
  moneyOut: z.number(),
  net: z.number(),
  pendingCount: z.number(),
})

export const ledgerPageSchema = z.object({
  items: z.array(ledgerEntrySchema),
  page: z.number(),
  pageSize: z.number(),
  total: z.number(),
  totals: ledgerTotalsSchema,
})
export type LedgerPage = z.infer<typeof ledgerPageSchema>

export const cashflowPointSchema = z.object({
  period: z.string(),
  moneyIn: z.number(),
  moneyOut: z.number(),
  net: z.number(),
})
export type CashflowPoint = z.infer<typeof cashflowPointSchema>

export const overviewSchema = z.object({
  from: z.string(),
  to: z.string(),
  moneyIn: z.number(),
  moneyOut: z.number(),
  net: z.number(),
  pendingCount: z.number(),
  pendingAmount: z.number(),
  cashflow: z.array(cashflowPointSchema),
  funds: z.array(fundSchema),
  canPost: z.boolean(),
  canApprove: z.boolean(),
})
export type FinanceOverview = z.infer<typeof overviewSchema>

export const reconciliationSchema = z.object({
  id: z.string(),
  year: z.number(),
  month: z.number(),
  statementBalance: z.number(),
  bookBalance: z.number(),
  difference: z.number(),
  isBalanced: z.boolean(),
  note: z.string().nullable(),
  reconciledBy: z.string().nullable(),
  reconciledAt: z.string(),
})
export type Reconciliation = z.infer<typeof reconciliationSchema>

export const accountBalanceSchema = z.object({
  id: z.string(),
  label: z.string(),
  kind: z.string(),
  accountNumberMasked: z.string(),
  openingBalance: z.number(),
  bookBalance: z.number(),
  isReconcilable: z.boolean(),
  isActive: z.boolean(),
  lastReconciliation: reconciliationSchema.nullable(),
})
export type AccountBalance = z.infer<typeof accountBalanceSchema>

export const monthBlockerSchema = z.object({ code: z.string(), message: z.string() })

export const monthStatusSchema = z.object({
  year: z.number(),
  month: z.number(),
  label: z.string(),
  isClosed: z.boolean(),
  closedAt: z.string().nullable(),
  closedBy: z.string().nullable(),
  canClose: z.boolean(),
  canReopen: z.boolean(),
  /** Why it cannot be closed yet. The dialog shows these back, rather than a bare "no". */
  blockers: z.array(monthBlockerSchema),
})
export type MonthStatus = z.infer<typeof monthStatusSchema>

export const balancingSchema = z.object({
  year: z.number(),
  month: z.number(),
  accounts: z.array(accountBalanceSchema),
  period: monthStatusSchema,
  canPost: z.boolean(),
})
export type Balancing = z.infer<typeof balancingSchema>

export const budgetLineSchema = z.object({
  id: z.string(),
  categoryId: z.string(),
  categoryName: z.string(),
  kind: budgetLineKindSchema,
  label: z.string(),
  amountBudgeted: z.number(),
  actual: z.number(),
  variance: z.number(),
  sortOrder: z.number(),
  note: z.string().nullable(),
})
export type BudgetLine = z.infer<typeof budgetLineSchema>

export const budgetVersionSchema = z.object({
  id: z.string(),
  versionNumber: z.number(),
  reason: z.string(),
  createdBy: z.string().nullable(),
  createdAt: z.string(),
})
export type BudgetVersion = z.infer<typeof budgetVersionSchema>

export const budgetSchema = z.object({
  id: z.string().nullable(),
  eventId: z.string(),
  eventName: z.string(),
  fundId: z.string().nullable(),
  fundBalance: z.number(),
  version: budgetVersionSchema.nullable(),
  lines: z.array(budgetLineSchema),
  totals: z.object({
    incomeBudgeted: z.number(),
    incomeActual: z.number(),
    expenseBudgeted: z.number(),
    expenseActual: z.number(),
    netBudgeted: z.number(),
    netActual: z.number(),
  }),
  history: z.array(budgetVersionSchema),
  canEdit: z.boolean(),
})
export type EventBudget = z.infer<typeof budgetSchema>

export const expenseRequestSchema = z.object({
  id: z.string(),
  title: z.string(),
  description: z.string().nullable(),
  amount: z.number(),
  categoryId: z.string(),
  categoryName: z.string(),
  neededBy: z.string().nullable(),
  requestedBy: z.string().nullable(),
  requestedAt: z.string(),
  status: requestStatusSchema,
  decidedBy: z.string().nullable(),
  decidedAt: z.string().nullable(),
  decisionReason: z.string().nullable(),
  paidAt: z.string().nullable(),
  paidEntryId: z.string().nullable(),
  cancelReason: z.string().nullable(),
  canApprove: z.boolean(),
  canReject: z.boolean(),
  canPay: z.boolean(),
  canCancel: z.boolean(),
})
export type ExpenseRequest = z.infer<typeof expenseRequestSchema>

export const expenseRequestPageSchema = z.object({
  items: z.array(expenseRequestSchema),
  page: z.number(),
  pageSize: z.number(),
  total: z.number(),
})
export type ExpenseRequestPage = z.infer<typeof expenseRequestPageSchema>

export const financeAuditSchema = z.object({
  id: z.string(),
  at: z.string(),
  actor: z.string().nullable(),
  action: z.string(),
  entityType: z.string(),
  entityId: z.string(),
  beforeJson: z.string().nullable(),
  afterJson: z.string().nullable(),
  reason: z.string().nullable(),
})
export type FinanceAudit = z.infer<typeof financeAuditSchema>

export const financeAuditPageSchema = z.object({
  items: z.array(financeAuditSchema),
  page: z.number(),
  pageSize: z.number(),
  total: z.number(),
})
export type FinanceAuditPage = z.infer<typeof financeAuditPageSchema>

export const periodsSchema = z.array(monthStatusSchema)

// ---- Forms -----------------------------------------------------------------------------

const isoDate = /^\d{4}-\d{2}-\d{2}$/
const dateField = z.string().regex(isoDate, 'Pick a date.')

/** Money is a text field parsed here, so the browser never rounds it for us. */
const amountField = z.string().superRefine((v, ctx) => {
  if (parseAmount(v) === null) ctx.addIssue({ code: 'custom', message: AMOUNT_HINT })
})

export const entryFormSchema = z
  .object({
    fundId: z.string().min(1, 'Choose a fund.'),
    paymentAccountId: z.string().min(1, 'Choose an account.'),
    categoryId: z.string().min(1, 'Choose a category.'),
    entryDate: dateField,
    direction: directionSchema,
    amount: amountField,
    reference: z.string().max(100, 'Keep the reference under 100 characters.'),
    description: z.string().trim().min(1, 'Say what the entry is for.').max(300, 'Keep it under 300 characters.'),
  })
  .transform((v) => ({ ...v, amount: parseAmount(v.amount) ?? 0 }))

export type EntryFormInput = z.input<typeof entryFormSchema>
export type EntryFormBody = z.output<typeof entryFormSchema>

export const transferFormSchema = z
  .object({
    fromFundId: z.string().min(1, 'Choose a fund to transfer from.'),
    toFundId: z.string().min(1, 'Choose a fund to transfer to.'),
    entryDate: dateField,
    amount: amountField,
    description: z.string().trim().min(1, 'Say what the transfer is for.').max(300, 'Keep it under 300 characters.'),
  })
  .refine((v) => v.fromFundId !== v.toFundId, {
    message: 'Pick two different funds for a transfer.',
    path: ['toFundId'],
  })
  .transform((v) => ({ ...v, amount: parseAmount(v.amount) ?? 0 }))

export type TransferFormInput = z.input<typeof transferFormSchema>
export type TransferFormBody = z.output<typeof transferFormSchema>

export const reconcileFormSchema = z
  .object({
    /** A statement balance may be negative (overdrawn) or zero (an emptied cash box). */
    statementBalance: z.string().superRefine((v, ctx) => {
      if (parseSignedAmount(v) === null) ctx.addIssue({ code: 'custom', message: SIGNED_AMOUNT_HINT })
    }),
    note: z.string().max(500, 'Keep the note under 500 characters.'),
  })
  .transform((v) => ({ statementBalance: parseSignedAmount(v.statementBalance) ?? 0, note: v.note }))

export type ReconcileFormInput = z.input<typeof reconcileFormSchema>
export type ReconcileFormBody = z.output<typeof reconcileFormSchema>

export const requestFormSchema = z
  .object({
    title: z.string().trim().min(1, 'Say what the money is for.').max(120, 'Keep it under 120 characters.'),
    description: z.string().max(1000, 'Keep it under 1000 characters.'),
    amount: amountField,
    categoryId: z.string().min(1, 'Choose a category.'),
    neededBy: z.string().refine((v) => v === '' || isoDate.test(v), 'Pick a date.'),
  })
  .transform((v) => ({
    ...v,
    amount: parseAmount(v.amount) ?? 0,
    description: v.description.trim() === '' ? null : v.description.trim(),
    neededBy: v.neededBy === '' ? null : v.neededBy,
  }))

export type RequestFormInput = z.input<typeof requestFormSchema>
export type RequestFormBody = z.output<typeof requestFormSchema>

export const reasonFormSchema = z.object({
  reason: z.string().trim().min(1, 'A reason is required.').max(500, 'Keep it under 500 characters.'),
})
export type ReasonFormInput = z.input<typeof reasonFormSchema>

export const budgetLineFormSchema = z.object({
  categoryId: z.string().min(1, 'Choose a category.'),
  kind: budgetLineKindSchema,
  label: z.string().trim().min(1, 'Name the line.').max(120, 'Keep it under 120 characters.'),
  amountBudgeted: amountField,
  note: z.string().max(300, 'Keep the note under 300 characters.'),
})

export const budgetFormSchema = z
  .object({
    reason: z.string().trim().min(1, 'Say why the budget changed.').max(500, 'Keep it under 500 characters.'),
    lines: z.array(budgetLineFormSchema).min(1, 'A budget needs at least one line.'),
  })
  .superRefine((v, ctx) => {
    // The same category twice on one side would make an actual ambiguous.
    const seen = new Set<string>()
    v.lines.forEach((line, i) => {
      const key = `${line.categoryId}|${line.kind}`
      if (seen.has(key)) {
        ctx.addIssue({
          code: 'custom',
          path: ['lines', i, 'categoryId'],
          message: 'This category is already on this side. Combine the lines.',
        })
      }
      seen.add(key)
    })
  })
  .transform((v) => ({
    reason: v.reason,
    lines: v.lines.map((l, i) => ({
      categoryId: l.categoryId,
      kind: l.kind,
      label: l.label,
      amountBudgeted: parseAmount(l.amountBudgeted) ?? 0,
      sortOrder: i * 10,
      note: l.note.trim() === '' ? null : l.note.trim(),
    })),
  }))

export type BudgetFormInput = z.input<typeof budgetFormSchema>
export type BudgetFormBody = z.output<typeof budgetFormSchema>
