import type {
  AccountBalance,
  Balancing,
  EventBudget,
  ExpenseRequest,
  ExpenseRequestPage,
  FinanceAuditPage,
  FinanceOverview,
  Fund,
  LedgerCategory,
  LedgerEntry,
  LedgerPage,
  MonthStatus,
} from '@/features/finance/schemas'

export const orgFund: Fund = {
  id: 'fund-org',
  kind: 'Organization',
  eventId: null,
  name: 'Organization',
  balance: 125_000,
  isClosed: false,
  isSwept: false,
  sweptAt: null,
  canSweep: false,
}

export const eventFund: Fund = {
  id: 'fund-evt',
  kind: 'Event',
  eventId: 'evt-1',
  name: 'HTN Con 2026',
  balance: 42_000.5,
  isClosed: false,
  isSwept: false,
  sweptAt: null,
  canSweep: true,
}

export const categories: LedgerCategory[] = [
  { id: 'cat-tickets', code: 'tickets', name: 'Ticket sales', kind: 'Income', isSystem: true, isActive: true },
  { id: 'cat-writeoff', code: 'write_off', name: 'Write-off', kind: 'Expense', isSystem: true, isActive: true },
]

export const gcash: AccountBalance = {
  id: 'acct-gcash',
  label: 'GCash main',
  kind: 'GCash',
  accountNumberMasked: '•••• 4567',
  openingBalance: 1_000,
  bookBalance: 43_000.5,
  isReconcilable: true,
  isActive: true,
  lastReconciliation: null,
}

export const cashOnHand: AccountBalance = {
  id: 'acct-cash',
  label: 'Cash on hand',
  kind: 'Cash',
  accountNumberMasked: '••••',
  openingBalance: 0,
  bookBalance: 2_500,
  isReconcilable: true,
  isActive: true,
  lastReconciliation: {
    id: 'rec-1',
    year: 2026,
    month: 10,
    statementBalance: 2_500,
    bookBalance: 2_500,
    difference: 0,
    isBalanced: true,
    note: 'Counted at the desk',
    reconciledBy: 'Noel Cruz',
    reconciledAt: '2026-10-31T10:00:00Z',
  },
}

export function entry(overrides: Partial<LedgerEntry> = {}): LedgerEntry {
  return {
    id: 'entry-1',
    fundId: orgFund.id,
    fundName: orgFund.name,
    paymentAccountId: gcash.id,
    accountLabel: gcash.label,
    categoryId: 'cat-tickets',
    categoryName: 'Ticket sales',
    entryDate: '2026-10-05',
    direction: 'In',
    amount: 2_500,
    signedAmount: 2_500,
    reference: 'OR-1001',
    description: 'Ticket payment · HTN-ABC123',
    hasReceipt: true,
    status: 'Posted',
    source: 'TicketPayment',
    transferGroupId: null,
    contraOfEntryId: null,
    isInClosedMonth: false,
    enteredBy: 'Noel Cruz',
    enteredAt: '2026-10-05T04:00:00Z',
    approvedBy: 'Noel Cruz',
    approvedAt: '2026-10-05T04:00:00Z',
    voidedBy: null,
    voidedAt: null,
    voidReason: null,
    canApprove: false,
    canVoid: false,
    ...overrides,
  }
}

export function ledgerPage(items: LedgerEntry[] = [entry()]): LedgerPage {
  const posted = items.filter((e) => e.status === 'Posted')
  const moneyIn = posted.filter((e) => e.direction === 'In').reduce((s, e) => s + e.amount, 0)
  const moneyOut = posted.filter((e) => e.direction === 'Out').reduce((s, e) => s + e.amount, 0)
  return {
    items,
    page: 1,
    pageSize: 50,
    total: items.length,
    totals: {
      moneyIn,
      moneyOut,
      net: moneyIn - moneyOut,
      pendingCount: items.filter((e) => e.status === 'Pending').length,
    },
  }
}

export const overview: FinanceOverview = {
  from: '2026-01-01',
  to: '2026-10-31',
  moneyIn: 180_000,
  moneyOut: 55_000,
  net: 125_000,
  pendingCount: 1,
  pendingAmount: 900,
  cashflow: [
    { period: '2026-09-01', moneyIn: 80_000, moneyOut: 20_000, net: 60_000 },
    { period: '2026-10-01', moneyIn: 100_000, moneyOut: 35_000, net: 65_000 },
  ],
  funds: [orgFund, eventFund],
  canPost: true,
  canApprove: true,
}

export function period(overrides: Partial<MonthStatus> = {}): MonthStatus {
  return {
    year: 2026,
    month: 10,
    label: 'October 2026',
    isClosed: false,
    closedAt: null,
    closedBy: null,
    canClose: true,
    canReopen: false,
    blockers: [],
    ...overrides,
  }
}

export function balancing(overrides: Partial<Balancing> = {}): Balancing {
  return {
    year: 2026,
    month: 10,
    accounts: [gcash, cashOnHand],
    period: period(),
    canPost: true,
    ...overrides,
  }
}

export const auditPage: FinanceAuditPage = {
  items: [
    {
      id: 'audit-1',
      at: '2026-10-31T12:00:00Z',
      actor: 'Noel Cruz',
      action: 'finance.month_close',
      entityType: 'MonthClose',
      entityId: 'close-1',
      beforeJson: null,
      afterJson: '{"year":2026,"month":10}',
      reason: null,
    },
  ],
  page: 1,
  pageSize: 50,
  total: 1,
}

export function budget(overrides: Partial<EventBudget> = {}): EventBudget {
  return {
    id: 'budget-1',
    eventId: 'evt-1',
    eventName: 'HTN Con 2026',
    fundId: eventFund.id,
    fundBalance: 42_000.5,
    version: {
      id: 'ver-2',
      versionNumber: 2,
      reason: 'The venue quote went up',
      createdBy: 'Noel Cruz',
      createdAt: '2026-10-20T02:00:00Z',
    },
    lines: [
      {
        id: 'line-1',
        categoryId: 'cat-tickets',
        categoryName: 'Ticket sales',
        kind: 'Income',
        label: 'Ticket sales',
        amountBudgeted: 100_000,
        actual: 120_000,
        variance: 20_000,
        sortOrder: 0,
        note: null,
      },
      {
        id: 'line-2',
        categoryId: 'cat-writeoff',
        categoryName: 'Write-off',
        kind: 'Expense',
        label: 'Venue',
        amountBudgeted: 60_000,
        actual: 78_000,
        variance: -18_000,
        sortOrder: 10,
        note: null,
      },
    ],
    totals: {
      incomeBudgeted: 100_000,
      incomeActual: 120_000,
      expenseBudgeted: 60_000,
      expenseActual: 78_000,
      netBudgeted: 40_000,
      netActual: 42_000,
    },
    history: [
      { id: 'ver-2', versionNumber: 2, reason: 'The venue quote went up', createdBy: 'Noel Cruz', createdAt: '2026-10-20T02:00:00Z' },
      { id: 'ver-1', versionNumber: 1, reason: 'First draft', createdBy: 'Noel Cruz', createdAt: '2026-10-01T02:00:00Z' },
    ],
    canEdit: true,
    ...overrides,
  }
}

export function request(overrides: Partial<ExpenseRequest> = {}): ExpenseRequest {
  return {
    id: 'req-1',
    title: 'Year-end party',
    description: 'Venue and food for 40',
    amount: 40_000,
    categoryId: 'cat-writeoff',
    categoryName: 'Write-off',
    neededBy: '2026-12-15',
    requestedBy: 'Alex Reyes',
    requestedAt: '2026-10-10T02:00:00Z',
    status: 'Pending',
    decidedBy: null,
    decidedAt: null,
    decisionReason: null,
    paidAt: null,
    paidEntryId: null,
    cancelReason: null,
    canApprove: false,
    canReject: false,
    canPay: false,
    canCancel: false,
    ...overrides,
  }
}

export function requestPage(items: ExpenseRequest[] = [request()]): ExpenseRequestPage {
  return { items, page: 1, pageSize: 25, total: items.length }
}

/** Every finance read, answered with the fixtures above. Individual tests override what they care about. */
export function financeHandlers(
  http: typeof import('msw').http,
  json: (body: Record<string, unknown> | unknown[]) => Response,
) {
  return [
    http.get('/api/finance/overview', () => json(overview)),
    http.get('/api/finance/funds', () => json([orgFund, eventFund])),
    http.get('/api/finance/categories', () => json(categories)),
    http.get('/api/finance/ledger', () => json(ledgerPage())),
    http.get('/api/finance/accounts', () => json(balancing())),
    http.get('/api/finance/periods', () => json([period()])),
    http.get('/api/finance/audit', () => json(auditPage)),
    http.get('/api/finance/budgets/:eventId', () => json(budget())),
    http.get('/api/finance/requests', () => json(requestPage())),
  ]
}
