import { useState } from 'react'
import { Badge } from '@/components/ui/Badge'
import { Card, CardHeader } from '@/components/ui/Card'
import { Stepper } from '@/components/ui/Stepper'
import { useUpdateDiscountsMutation } from '../api'
import type { EventMerchSettings } from '../schemas'
import { levelBadgeColor } from './labels'

type Level = 'Goon' | 'Apprentice' | 'Member' | 'Board'
const levels: Level[] = ['Goon', 'Apprentice', 'Member', 'Board']
const fieldFor: Record<Level, keyof EventMerchSettings> = {
  Goon: 'goonPct',
  Apprentice: 'apprenticePct',
  Member: 'memberPct',
  Board: 'boardPct',
}

/** Discounts aside (canvas frame 20, right column). Read-only percentages when the caller can't manage. */
export function DiscountsPanel({ eventId, settings, canManage }: { eventId: string; settings: EventMerchSettings; canManage: boolean }) {
  const update = useUpdateDiscountsMutation(eventId)
  // Optimistic local echo so the stepper feels instant; the query refetch reconciles it.
  const [local, setLocal] = useState(settings)
  const current = update.isPending ? local : settings

  function setPct(level: Level, value: number) {
    const next = { ...settings, [fieldFor[level]]: value }
    setLocal(next)
    update.mutate({ goonPct: next.goonPct, apprenticePct: next.apprenticePct, memberPct: next.memberPct, boardPct: next.boardPct })
  }

  return (
    <Card>
      <CardHeader title="Discounts for this event" description="Applied automatically when the buyer is identified in POS." />
      <div className="flex flex-col gap-3 p-5">
        {levels.map((level) => {
          const pct = current[fieldFor[level]] as number
          return (
            <div key={level} className="flex items-center justify-between gap-3">
              <Badge color={levelBadgeColor(level)}>{level}</Badge>
              {canManage ? (
                <Stepper name={`${level} discount`} value={pct} onChange={(v) => setPct(level, v)} min={0} max={100} step={5} />
              ) : (
                <output className="text-sm font-semibold tabular-nums text-gray-900 dark:text-gray-50">{pct}%</output>
              )}
            </div>
          )
        })}
        <div className="flex items-center justify-between gap-3 border-t border-gray-100 pt-3 dark:border-white/10">
          <span className="text-sm text-gray-600 dark:text-gray-400">Attendees &amp; walk-ins</span>
          <span className="text-sm font-semibold text-gray-900 dark:text-gray-50">Full price</span>
        </div>
      </div>
    </Card>
  )
}
