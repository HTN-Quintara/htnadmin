import { useState } from 'react'
import { useAuth, useCan } from '@/app/auth/auth-context'
import { Policies } from '@/app/auth/policies'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { useBalancingQuery } from './api'
import { AuditView } from './components/AuditView'
import { BalancingView } from './components/BalancingView'
import { CashflowView } from './components/CashflowView'
import { LedgerView } from './components/LedgerView'

type View = 'cashflow' | 'ledger' | 'balancing' | 'audit'

/**
 * Frame 24. Four sub-views behind a segmented control, as the design system prescribes; the ledger is the one the
 * finance assistant can see, so it is their default and the only tab they are offered.
 */
export function FinancePage() {
  const { me } = useAuth()
  const isCfo = me?.isCfo ?? false
  const isAssistant = me?.isFinanceAssistant ?? false
  const isBoard = me?.level === 'Board'
  const canPost = useCan(Policies.FinancePost)

  const [view, setView] = useState<View>(isBoard ? 'cashflow' : 'ledger')

  // The accounts list doubles as the ledger's account filter and the entry form's options.
  const now = new Date()
  const balancing = useBalancingQuery({ year: now.getFullYear(), month: now.getMonth() + 1 })
  const accounts = (balancing.data?.accounts ?? []).map((a) => ({
    id: a.id,
    label: a.label,
    isActive: a.isActive,
  }))

  const options: { value: View; label: string }[] = isBoard
    ? [
        { value: 'cashflow', label: 'Cashflow' },
        { value: 'ledger', label: 'Ledger' },
        { value: 'balancing', label: 'Balancing' },
        { value: 'audit', label: 'Audit' },
      ]
    : [{ value: 'ledger', label: 'Ledger' }]

  return (
    <div className="flex flex-col gap-5">
      {options.length > 1 ? (
        <SegmentedControl options={options} value={view} onChange={setView} aria-label="Finance views" />
      ) : null}

      {view === 'cashflow' ? <CashflowView /> : null}
      {view === 'ledger' ? (
        <LedgerView canPost={canPost} canCreate={isCfo || isAssistant} isCfo={isCfo} accounts={accounts} />
      ) : null}
      {view === 'balancing' ? <BalancingView /> : null}
      {view === 'audit' ? <AuditView canExport={isBoard} /> : null}
    </div>
  )
}
