import { useMemo, useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardBody } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { FilterChip } from '@/components/ui/FilterChip'
import { Skeleton } from '@/components/ui/Spinner'
import { Stat } from '@/components/ui/Stat'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { Badge } from '@/components/ui/Badge'
import { BuildingIcon } from '@/components/ui/icons'
import { useSponsorsPageQuery } from '../api'
import type { Sponsor } from '../schemas'
import { SponsorStatusBadge } from './badges'
import { SponsorDialog } from './SponsorDialog'
import { TierDialog } from './TierDialog'
import { SponsorDrawer } from './SponsorDrawer'

type Filter = 'All' | 'Cash' | 'In-kind' | 'Cash + In-kind' | 'Individuals'
const filters: Filter[] = ['All', 'Cash', 'In-kind', 'Cash + In-kind', 'Individuals']

export function SponsorsView({ eventId }: { eventId: string }) {
  const query = useSponsorsPageQuery(eventId)
  const [filter, setFilter] = useState<Filter>('All')
  const [newSponsor, setNewSponsor] = useState(false)
  const [newTier, setNewTier] = useState(false)
  const [openId, setOpenId] = useState<string | null>(null)

  const sponsors = useMemo(() => {
    const all = query.data?.sponsors ?? []
    return all.filter((s) => {
      if (filter === 'All') return true
      if (filter === 'Individuals') return s.kind === 'Individual'
      return s.contributionType === filter && s.kind === 'Organization'
    })
  }, [query.data, filter])

  if (query.isPending) {
    return (
      <div role="status" aria-label="Loading sponsors" className="flex flex-col gap-4">
        <Skeleton className="h-24 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    )
  }
  if (query.isError) {
    return (
      <Alert tone="red" action={<Button size="sm" variant="secondary" onClick={() => query.refetch()}>Try again</Button>}>
        Could not load sponsors. {errorMessage(query.error)}
      </Alert>
    )
  }

  const page = query.data

  return (
    <div className="flex flex-col gap-6">
      <section aria-label="Sponsorship totals" className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {page.kpis.map((k) => (
          <Card key={k.label}>
            <CardBody>
              <Stat label={k.label} value={k.value} sub={k.sub} />
            </CardBody>
          </Card>
        ))}
      </section>

      <section className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-base font-semibold text-gray-900 dark:text-gray-50">Tiers</h2>
          <Button size="sm" variant="secondary" onClick={() => setNewTier(true)}>+ Tier</Button>
        </div>
        {page.tiers.length === 0 ? (
          <p className="text-sm text-gray-500 dark:text-gray-400">No tiers yet. Add one to start signing sponsors.</p>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {page.tiers.map((t) => (
              <Card key={t.id}>
                <CardBody className="flex flex-col gap-2">
                  <div className="flex items-center justify-between">
                    <Badge color="brand">{t.class}</Badge>
                    <span className="text-xs text-gray-500 dark:text-gray-400">{t.slotsTaken} of {t.slots} taken</span>
                  </div>
                  <div>
                    <h3 className="font-semibold text-gray-900 dark:text-gray-50">{t.name}</h3>
                    <p className="text-sm text-gray-500 dark:text-gray-400">{t.priceText}</p>
                  </div>
                  {t.perks.length > 0 && (
                    <ul className="list-disc pl-5 text-sm text-gray-600 dark:text-gray-300">
                      {t.perks.map((p) => <li key={p}>{p}</li>)}
                    </ul>
                  )}
                  <p className="text-xs text-gray-500 dark:text-gray-400">
                    {t.compedSeats > 0 ? `${t.compedSeats} comped participants` : 'No comped participants'}
                  </p>
                </CardBody>
              </Card>
            ))}
          </div>
        )}
      </section>

      <section className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-base font-semibold text-gray-900 dark:text-gray-50">Sponsors</h2>
          <Button size="sm" onClick={() => setNewSponsor(true)}>+ Sponsor</Button>
        </div>
        <div className="flex flex-wrap gap-2">
          {filters.map((f) => (
            <FilterChip key={f} selected={filter === f} onClick={() => setFilter(f)}>{f}</FilterChip>
          ))}
        </div>

        {sponsors.length === 0 ? (
          <EmptyState
            icon={<BuildingIcon />}
            title="No sponsors yet"
            description="Sign your first sponsor to a tier, or add an individual supporter."
            action={<Button onClick={() => setNewSponsor(true)}>+ Sponsor</Button>}
          />
        ) : (
          <div className="overflow-x-auto">
            <Table minWidth={820}>
              <THead>
                <Tr>
                  <Th>Sponsor</Th>
                  <Th>Status</Th>
                  <Th>Tier</Th>
                  <Th>Contribution</Th>
                  <Th>Logo &amp; website</Th>
                  <Th className="text-right">Comped</Th>
                  <Th className="text-right">Perks</Th>
                </Tr>
              </THead>
              <TBody>
                {sponsors.map((s) => (
                  <Tr key={s.id} className="cursor-pointer" onClick={() => setOpenId(s.id)}>
                    <Td>
                      <div className="flex flex-col">
                        <button type="button" className="text-left font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300">
                          {s.isAnonymous ? 'Anonymous donor' : s.name}
                        </button>
                        <span className="text-xs text-gray-500 dark:text-gray-400">{contactLine(s)}</span>
                      </div>
                    </Td>
                    <Td><SponsorStatusBadge status={s.status} /></Td>
                    <Td>{s.tierName ? <span className="text-sm">{s.tierClass} · {s.tierName}</span> : <span className="text-gray-400">—</span>}</Td>
                    <Td>
                      <div className="flex flex-col">
                        <span className="text-xs text-gray-500 dark:text-gray-400">{s.contributionType}</span>
                        <span className="text-sm">{s.amountText}</span>
                      </div>
                    </Td>
                    <Td>{assetsCell(s)}</Td>
                    <Td numeric>{s.compsEntitlement > 0 ? `${s.compsUsed} / ${s.compsEntitlement}` : '—'}</Td>
                    <Td numeric>{s.perksTotal > 0 ? `${s.perksDelivered} / ${s.perksTotal}` : '—'}</Td>
                  </Tr>
                ))}
              </TBody>
            </Table>
          </div>
        )}
      </section>

      <SponsorDialog eventId={eventId} tiers={page.tiers} open={newSponsor} onClose={() => setNewSponsor(false)} />
      <TierDialog eventId={eventId} open={newTier} onClose={() => setNewTier(false)} />
      <SponsorDrawer eventId={eventId} sponsorId={openId} tiers={page.tiers} onClose={() => setOpenId(null)} />
    </div>
  )
}

function contactLine(s: Sponsor): string {
  if (s.kind === 'Individual') return s.isAnonymous ? 'Anonymous' : `Credited as “${s.displayName ?? s.name}”`
  return [s.contactRole, s.contactName].filter(Boolean).join(' · ') || 'Organization'
}

function assetsCell(s: Sponsor) {
  if (s.kind === 'Individual') return <span className="text-xs text-gray-500 dark:text-gray-400">{s.isAnonymous ? 'Anonymous' : `Credited as “${s.displayName ?? s.name}”`}</span>
  if (s.logoFileId && s.websiteUrl) return <span className="text-xs text-green-700 dark:text-green-400">✓ Logo &amp; website</span>
  if (s.status !== 'Confirmed') return <span className="text-xs text-gray-400">After confirmation</span>
  return <span className="text-xs text-amber-700 dark:text-amber-400">Needs logo or website</span>
}
