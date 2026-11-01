import { useState } from 'react'
import { useAuth } from '@/app/auth/auth-context'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { PageHeader } from '@/components/ui/PageHeader'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { Skeleton } from '@/components/ui/Spinner'
import { usePopUpEligibleCommittees, usePopUpsQuery, useLeftoverInventory } from './api'
import { AllocationDialog } from './components/AllocationDialog'
import { CommitteePopUpToggle } from './components/CommitteePopUpToggle'
import { LeftoverInventoryTable } from './components/LeftoverInventoryTable'
import { NewPopUpDialog } from './components/NewPopUpDialog'
import { PopUpDetail } from './components/PopUpDetail'
import { PopUpList } from './components/PopUpList'
import type { PopUp } from './schemas'

type Tab = 'popUps' | 'leftover'

/** Merch store: leftover inventory + pop-up stores (canvas PopUps.dc.html, route `/store`). */
export function MerchStorePage() {
  const { me } = useAuth()
  const isBoard = me?.level === 'Board'
  const [tab, setTab] = useState<Tab>('popUps')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [justCreated, setJustCreated] = useState<PopUp | null>(null)

  const popUps = usePopUpsQuery()
  const leftover = useLeftoverInventory()
  const committees = usePopUpEligibleCommittees()

  const selected = popUps.data?.find((p) => p.id === selectedId) ?? popUps.data?.[0] ?? null
  const totalLeftoverPcs = (leftover.data ?? []).reduce((n, v) => n + v.stockOnHand, 0)

  const tabOptions: Array<{ value: Tab; label: string }> = [
    { value: 'popUps', label: `Pop-up stores · ${popUps.data?.length ?? 0}` },
    { value: 'leftover', label: `Leftover inventory · ${totalLeftoverPcs} pcs` },
  ]

  return (
    <div className="flex flex-col gap-5 p-4 sm:p-6">
      <PageHeader
        title="Merch store"
        description="Leftover stock from events lives here. Sell it at pop-up stores; sales go to the Organization fund."
        actions={<Button onClick={() => setCreating(true)}>+ Pop-up store</Button>}
      />

      {isBoard ? (
        <Card>
          <CardHeader title="Who can create pop-ups" description="Admins choose which committees may run pop-up stores." />
          <div className="flex flex-col gap-3 p-5">
            {committees.isPending ? <Skeleton className="h-9 w-full" /> : null}
            {committees.isError ? (
              <Alert tone="red" live="alert" action={<Button size="sm" variant="secondary" onClick={() => committees.refetch()}>Try again</Button>}>
                Could not load committees.
              </Alert>
            ) : null}
            {!committees.isPending && !committees.isError && committees.committees.length === 0 ? (
              <p className="text-sm text-gray-600 dark:text-gray-400">No active committees yet.</p>
            ) : null}
            {committees.committees.length > 0 ? (
              <div className="flex flex-wrap gap-2">
                {committees.committees.map((c) => (
                  <CommitteePopUpToggle key={`${c.eventId}-${c.committeeId}`} committee={c} />
                ))}
              </div>
            ) : null}
          </div>
        </Card>
      ) : null}

      <SegmentedControl options={tabOptions} value={tab} onChange={setTab} aria-label="Merch store views" />

      {tab === 'popUps' ? (
        <>
          {popUps.isPending ? (
            <div role="status" aria-label="Loading pop-up stores">
              <Skeleton className="h-64 w-full" />
            </div>
          ) : null}
          {popUps.isError ? (
            <Alert tone="red" live="alert" action={<Button size="sm" variant="secondary" onClick={() => void popUps.refetch()}>Try again</Button>}>
              Could not load pop-up stores. {errorMessage(popUps.error)}
            </Alert>
          ) : null}
          {popUps.data ? (
            <div className="grid grid-cols-1 gap-5 lg:grid-cols-[1fr_1.4fr]">
              <PopUpList popUps={popUps.data} selectedId={selected?.id ?? null} onSelect={setSelectedId} />
              {selected ? <PopUpDetail popUp={selected} /> : null}
            </div>
          ) : null}
        </>
      ) : (
        <>
          {leftover.isPending ? (
            <div role="status" aria-label="Loading leftover inventory">
              <Skeleton className="h-64 w-full" />
            </div>
          ) : null}
          {leftover.isError ? (
            <Alert tone="red" live="alert" action={<Button size="sm" variant="secondary" onClick={() => void leftover.refetch()}>Try again</Button>}>
              Could not load leftover inventory. {errorMessage(leftover.error)}
            </Alert>
          ) : null}
          {leftover.data ? <LeftoverInventoryTable variants={leftover.data} /> : null}
        </>
      )}

      <NewPopUpDialog
        open={creating}
        onClose={() => setCreating(false)}
        onCreated={(popUp) => {
          setSelectedId(popUp.id)
          setJustCreated(popUp)
        }}
      />
      {justCreated ? <AllocationDialog popUp={justCreated} onClose={() => setJustCreated(null)} /> : null}
    </div>
  )
}
