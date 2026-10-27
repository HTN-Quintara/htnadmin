import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, apiRequest } from '@/app/api'
import {
  balancingSchema,
  budgetSchema,
  categorySchema,
  expenseRequestPageSchema,
  expenseRequestSchema,
  financeAuditPageSchema,
  fundSchema,
  ledgerEntrySchema,
  ledgerPageSchema,
  monthStatusSchema,
  overviewSchema,
  periodsSchema,
  type EntryFormBody,
  type LedgerStatus,
  type ReconcileFormBody,
  type RequestFormBody,
  type RequestStatus,
  type TransferFormBody,
  type BudgetFormBody,
} from './schemas'
import { z } from 'zod'

const enc = encodeURIComponent

export type LedgerFilter = {
  fundId?: string
  eventId?: string
  accountId?: string
  categoryId?: string
  status?: LedgerStatus
  direction?: 'In' | 'Out'
  from?: string
  to?: string
  search?: string
  page?: number
  pageSize?: number
}

export type PeriodFilter = { year: number; month: number }
export type AuditFilter = { action?: string; from?: string; to?: string; page?: number }
export type RequestFilter = { status?: RequestStatus; page?: number }

/**
 * Hierarchical, so a write can invalidate exactly the branch it touched. Everything money-related hangs off
 * `['finance']` because posting one entry moves the ledger, the overview, the balances and the periods at once.
 */
export const financeKeys = {
  all: ['finance'] as const,
  overview: (f: { from?: string; to?: string; fundId?: string }) => ['finance', 'overview', f] as const,
  funds: () => ['finance', 'funds'] as const,
  categories: () => ['finance', 'categories'] as const,
  ledgers: () => ['finance', 'ledger'] as const,
  ledger: (f: LedgerFilter) => ['finance', 'ledger', 'list', f] as const,
  entry: (id: string) => ['finance', 'ledger', 'detail', id] as const,
  accounts: (p: PeriodFilter) => ['finance', 'accounts', p] as const,
  periods: () => ['finance', 'periods'] as const,
  audit: (f: AuditFilter) => ['finance', 'audit', f] as const,
  budgets: () => ['finance', 'budgets'] as const,
  budget: (eventId: string) => ['finance', 'budgets', eventId] as const,
  budgetVersion: (eventId: string, versionId: string) => ['finance', 'budgets', eventId, 'version', versionId] as const,
  requests: () => ['finance', 'requests'] as const,
  requestList: (f: RequestFilter) => ['finance', 'requests', 'list', f] as const,
  request: (id: string) => ['finance', 'requests', 'detail', id] as const,
}

/** One place builds the ledger query string, so the list and the CSV export can never drift apart. */
export function ledgerQuery(f: LedgerFilter): string {
  const params = new URLSearchParams()
  if (f.fundId) params.set('fundId', f.fundId)
  if (f.eventId) params.set('eventId', f.eventId)
  if (f.accountId) params.set('accountId', f.accountId)
  if (f.categoryId) params.set('categoryId', f.categoryId)
  if (f.status) params.set('status', f.status)
  if (f.direction) params.set('direction', f.direction)
  if (f.from) params.set('from', f.from)
  if (f.to) params.set('to', f.to)
  if (f.search?.trim()) params.set('search', f.search.trim())
  if (f.page && f.page > 1) params.set('page', String(f.page))
  if (f.pageSize) params.set('pageSize', String(f.pageSize))
  const q = params.toString()
  return q ? `?${q}` : ''
}

export function exportLedgerCsvUrl(f: LedgerFilter): string {
  return `/api/finance/ledger/export.csv${ledgerQuery(f)}`
}

export function exportAuditCsvUrl(f: AuditFilter): string {
  const params = new URLSearchParams()
  if (f.action) params.set('action', f.action)
  if (f.from) params.set('from', f.from)
  if (f.to) params.set('to', f.to)
  const q = params.toString()
  return `/api/finance/audit/export.csv${q ? `?${q}` : ''}`
}

export function receiptUrl(entryId: string): string {
  return `/api/finance/ledger/${enc(entryId)}/receipt`
}

// ---- Reads ----------------------------------------------------------------------------

export function useOverviewQuery(f: { from?: string; to?: string; fundId?: string } = {}) {
  const params = new URLSearchParams()
  if (f.from) params.set('from', f.from)
  if (f.to) params.set('to', f.to)
  if (f.fundId) params.set('fundId', f.fundId)
  const q = params.toString()
  return useQuery({
    queryKey: financeKeys.overview(f),
    queryFn: () => api(`/api/finance/overview${q ? `?${q}` : ''}`, overviewSchema),
  })
}

export function useFundsQuery() {
  return useQuery({ queryKey: financeKeys.funds(), queryFn: () => api('/api/finance/funds', z.array(fundSchema)) })
}

export function useCategoriesQuery() {
  return useQuery({
    queryKey: financeKeys.categories(),
    queryFn: () => api('/api/finance/categories', z.array(categorySchema)),
    staleTime: 5 * 60_000, // the seeded list barely changes
  })
}

export function useLedgerQuery(f: LedgerFilter) {
  return useQuery({
    queryKey: financeKeys.ledger(f),
    queryFn: () => api(`/api/finance/ledger${ledgerQuery(f)}`, ledgerPageSchema),
    placeholderData: keepPreviousData,
  })
}

export function useBalancingQuery(p: PeriodFilter) {
  return useQuery({
    queryKey: financeKeys.accounts(p),
    queryFn: () => api(`/api/finance/accounts?year=${p.year}&month=${p.month}`, balancingSchema),
    placeholderData: keepPreviousData,
  })
}

export function usePeriodsQuery() {
  return useQuery({ queryKey: financeKeys.periods(), queryFn: () => api('/api/finance/periods', periodsSchema) })
}

export function useAuditQuery(f: AuditFilter) {
  const params = new URLSearchParams()
  if (f.action) params.set('action', f.action)
  if (f.from) params.set('from', f.from)
  if (f.to) params.set('to', f.to)
  if (f.page && f.page > 1) params.set('page', String(f.page))
  const q = params.toString()
  return useQuery({
    queryKey: financeKeys.audit(f),
    queryFn: () => api(`/api/finance/audit${q ? `?${q}` : ''}`, financeAuditPageSchema),
    placeholderData: keepPreviousData,
  })
}

export function useBudgetQuery(eventId: string) {
  return useQuery({
    queryKey: financeKeys.budget(eventId),
    queryFn: () => api(`/api/finance/budgets/${enc(eventId)}`, budgetSchema),
    enabled: !!eventId,
  })
}

export function useBudgetVersionQuery(eventId: string, versionId: string | null) {
  const id = versionId ?? ''
  return useQuery({
    queryKey: financeKeys.budgetVersion(eventId, id),
    queryFn: () => api(`/api/finance/budgets/${enc(eventId)}/versions/${enc(id)}`, budgetSchema),
    enabled: !!eventId && !!id,
  })
}

export function useRequestsQuery(f: RequestFilter) {
  const params = new URLSearchParams()
  if (f.status) params.set('status', f.status)
  if (f.page && f.page > 1) params.set('page', String(f.page))
  const q = params.toString()
  return useQuery({
    queryKey: financeKeys.requestList(f),
    queryFn: () => api(`/api/finance/requests${q ? `?${q}` : ''}`, expenseRequestPageSchema),
    placeholderData: keepPreviousData,
  })
}

// ---- Writes ---------------------------------------------------------------------------

/**
 * One entry moves several screens at once: the ledger list, the cashflow totals, the account balances and whether a
 * month can close. They are invalidated together rather than guessed at individually.
 */
function useFinanceSettled() {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: financeKeys.all })
}

export function useCreateEntryMutation() {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: async (body: EntryFormBody & { receipt: File }) => {
      // Multipart, because every hand-entered row carries its receipt.
      const form = new FormData()
      form.set('fundId', body.fundId)
      form.set('paymentAccountId', body.paymentAccountId)
      form.set('categoryId', body.categoryId)
      form.set('entryDate', body.entryDate)
      form.set('direction', body.direction)
      form.set('amount', String(body.amount))
      form.set('description', body.description)
      if (body.reference) form.set('reference', body.reference)
      form.set('receipt', body.receipt, body.receipt.name)
      const { data } = await apiRequest('/api/finance/ledger', { method: 'POST', body: form })
      return ledgerEntrySchema.parse(data)
    },
    onSuccess: () => settled(),
  })
}

export function useApproveEntryMutation() {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: (id: string) =>
      api(`/api/finance/ledger/${enc(id)}/approve`, ledgerEntrySchema, { method: 'POST' }),
    onSuccess: () => settled(),
  })
}

export function useVoidEntryMutation() {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      api(`/api/finance/ledger/${enc(id)}/void`, ledgerEntrySchema, { method: 'POST', body: { reason } }),
    onSuccess: () => settled(),
  })
}

export function useTransferMutation() {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: (body: TransferFormBody) =>
      api('/api/finance/ledger/transfers', ledgerEntrySchema, { method: 'POST', body }),
    onSuccess: () => settled(),
  })
}

export function useSweepMutation() {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: ({ fundId, entryDate }: { fundId: string; entryDate?: string }) =>
      api(`/api/finance/funds/${enc(fundId)}/sweep`, fundSchema, { method: 'POST', body: { entryDate } }),
    onSuccess: () => settled(),
  })
}

export function useReconcileMutation(accountId: string, period: PeriodFilter) {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: (body: ReconcileFormBody) =>
      api(`/api/finance/accounts/${enc(accountId)}/reconcile`, z.object({ id: z.string() }).loose(), {
        method: 'POST',
        body: { ...body, year: period.year, month: period.month },
      }),
    onSuccess: () => settled(),
  })
}

export function useCloseMonthMutation() {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: ({ year, month, note }: PeriodFilter & { note?: string }) =>
      api(`/api/finance/periods/${year}/${month}/close`, monthStatusSchema, { method: 'POST', body: { note } }),
    onSuccess: () => settled(),
  })
}

export function useReopenMonthMutation() {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: ({ year, month, reason }: PeriodFilter & { reason: string }) =>
      api(`/api/finance/periods/${year}/${month}/reopen`, monthStatusSchema, { method: 'POST', body: { reason } }),
    onSuccess: () => settled(),
  })
}

export function useSaveBudgetMutation(eventId: string) {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: (body: BudgetFormBody) =>
      api(`/api/finance/budgets/${enc(eventId)}`, budgetSchema, { method: 'PUT', body }),
    onSuccess: () => settled(),
  })
}

export function useCreateRequestMutation() {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: (body: RequestFormBody) =>
      api('/api/finance/requests', expenseRequestSchema, { method: 'POST', body }),
    onSuccess: () => settled(),
  })
}

export function useDecideRequestMutation() {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: ({ id, approve, reason }: { id: string; approve: boolean; reason?: string }) =>
      api(`/api/finance/requests/${enc(id)}/${approve ? 'approve' : 'reject'}`, expenseRequestSchema, {
        method: 'POST',
        body: { reason },
      }),
    onSuccess: () => settled(),
  })
}

export function usePayRequestMutation() {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: async ({
      id,
      paymentAccountId,
      entryDate,
      reference,
      receipt,
    }: {
      id: string
      paymentAccountId: string
      entryDate: string
      reference?: string
      receipt: File
    }) => {
      const form = new FormData()
      form.set('paymentAccountId', paymentAccountId)
      form.set('entryDate', entryDate)
      if (reference) form.set('reference', reference)
      form.set('receipt', receipt, receipt.name)
      const { data } = await apiRequest(`/api/finance/requests/${enc(id)}/pay`, { method: 'POST', body: form })
      return expenseRequestSchema.parse(data)
    },
    onSuccess: () => settled(),
  })
}

export function useCancelRequestMutation() {
  const settled = useFinanceSettled()
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      api(`/api/finance/requests/${enc(id)}/cancel`, expenseRequestSchema, { method: 'POST', body: { reason } }),
    onSuccess: () => settled(),
  })
}
