import { useState } from 'react'
import { Avatar } from '@/components/ui/Avatar'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Spinner } from '@/components/ui/Spinner'
import { initials } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { usePopUpBuyerSearch } from '../api'
import type { BuyerHit } from '../schemas'
import { levelBadgeColor } from './labels'

type Props = {
  popUpId: string
  buyer: BuyerHit | null
  discountPct: number | null
  onPick: (hit: BuyerHit) => void
  onClear: () => void
}

/**
 * Buyer section of a pop-up's "Current sale" (Phase 8C: a pop-up's own `/pos/buyer-search` is People only, and
 * there's no ticket QR to scan since a pop-up isn't tied to one event). Also accepts a free-text walk-in name.
 */
export function PopUpBuyerPanel({ popUpId, buyer, discountPct, onPick, onClear }: Props) {
  const [query, setQuery] = useState('')
  const [walkIn, setWalkIn] = useState('')
  const debounced = useDebouncedValue(query, 300)
  const search = usePopUpBuyerSearch(popUpId, debounced)

  if (buyer) {
    const subtitle = discountPct != null ? `${buyer.level} · ${discountPct}% merch discount` : buyer.level === 'Attendee' ? 'Full price' : `${buyer.level} discount applied at checkout`
    return (
      <div className="flex items-center gap-3 rounded-lg bg-gray-50 p-3 ring-1 ring-gray-200 dark:bg-white/5 dark:ring-white/10">
        <Avatar initials={initials(buyer.name.split(' ')[0] ?? '', buyer.name.split(' ').slice(-1)[0] ?? '')} tone="brand" size="lg" />
        <div className="flex min-w-0 flex-1 flex-col">
          <span className="truncate font-semibold text-gray-900 dark:text-gray-50">{buyer.name}</span>
          <span className="text-sm text-gray-600 dark:text-gray-400">{subtitle}</span>
        </div>
        <Button variant="secondary" size="sm" onClick={onClear}>
          Change
        </Button>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-3">
      <Input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search by name…" aria-label="Search buyer by name" />
      <p className="text-xs text-gray-600 dark:text-gray-400">Search any goon, apprentice, member or board account, or type a walk-in name below.</p>

      {search.isFetching && debounced.trim().length >= 3 ? <Spinner /> : null}
      {search.data && search.data.length > 0 ? (
        <ul className="flex flex-col gap-1">
          {search.data.map((hit) => (
            <li key={`${hit.kind}-${hit.personId ?? hit.attendeeId}`}>
              <button
                type="button"
                onClick={() => onPick(hit)}
                className="flex min-h-11 w-full items-center justify-between gap-2 rounded-md px-3 py-2 text-left text-sm hover:bg-gray-100 dark:hover:bg-white/10"
              >
                <span className="truncate text-gray-900 dark:text-gray-50">{hit.name}</span>
                <Badge color={levelBadgeColor(hit.level)}>{hit.level}</Badge>
              </button>
            </li>
          ))}
        </ul>
      ) : null}
      {search.data && search.data.length === 0 && debounced.trim().length >= 3 ? (
        <p className="text-sm text-gray-600 dark:text-gray-400">No one found. Try a different name, or add a walk-in buyer below.</p>
      ) : null}

      <div className="flex items-end gap-2 border-t border-gray-100 pt-3 dark:border-white/10">
        <Input
          value={walkIn}
          onChange={(e) => setWalkIn(e.target.value)}
          placeholder="Walk-in buyer's name"
          aria-label="Walk-in buyer's name"
          className="flex-1"
        />
        <Button
          variant="secondary"
          disabled={walkIn.trim().length === 0}
          onClick={() => onPick({ kind: 'Attendee', personId: null, attendeeId: null, name: walkIn.trim(), level: 'Attendee', email: null })}
        >
          Use walk-in
        </Button>
      </div>
    </div>
  )
}
