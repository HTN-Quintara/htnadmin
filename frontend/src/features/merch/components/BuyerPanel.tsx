import { useState } from 'react'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Avatar } from '@/components/ui/Avatar'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Spinner } from '@/components/ui/Spinner'
import { CameraIcon } from '@/components/ui/icons'
import { initials } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { QrCameraScanner } from '@/features/checkin/scanner/QrCameraScanner'
import { useBuyerSearch, useScanBuyerMutation } from '../api'
import type { BuyerHit } from '../schemas'
import { Badge } from '@/components/ui/Badge'
import { levelBadgeColor } from './labels'

type Props = {
  eventId: string
  buyer: BuyerHit | null
  discountPct: number | null
  onPick: (hit: BuyerHit) => void
  onClear: () => void
}

/** Buyer section of "Current sale" (canvas frame 21, right column). */
export function BuyerPanel({ eventId, buyer, discountPct, onPick, onClear }: Props) {
  const [query, setQuery] = useState('')
  const [scanning, setScanning] = useState(false)
  const debounced = useDebouncedValue(query, 300)
  const search = useBuyerSearch(eventId, debounced)
  const scan = useScanBuyerMutation(eventId)

  if (buyer) {
    const subtitle = buyer.level === 'Attendee' ? 'Full price' : discountPct != null ? `${buyer.level} · ${discountPct}% merch discount` : `${buyer.level} discount applied at checkout`
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
      {scanning ? (
        <div className="flex flex-col gap-2">
          <QrCameraScanner
            className="h-56 rounded-lg"
            caption="Point the camera at the ticket QR code"
            fallback="Search by name below instead."
            onDecode={(payload) => {
              if (scan.isPending) return
              scan.mutate(payload, {
                onSuccess: (hit) => {
                  setScanning(false)
                  onPick(hit)
                },
              })
            }}
          />
          {scan.isError && !isApiError(scan.error, 404) ? (
            <Alert tone="red" live="alert">
              {errorMessage(scan.error)}
            </Alert>
          ) : null}
          {isApiError(scan.error, 404) ? <Alert tone="yellow">That code isn't a valid ticket for this event.</Alert> : null}
          <Button variant="secondary" onClick={() => setScanning(false)}>
            Cancel scan
          </Button>
        </div>
      ) : (
        <Button variant="secondary" block onClick={() => setScanning(true)}>
          <CameraIcon size={18} />
          Scan QR
        </Button>
      )}

      <Input
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search by name…"
        aria-label="Search buyer by name"
      />
      <p className="text-xs text-gray-600 dark:text-gray-400">Scan a ticket QR, or search by name.</p>

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
        <p className="text-sm text-gray-600 dark:text-gray-400">No one found. Try a different name.</p>
      ) : null}
    </div>
  )
}
