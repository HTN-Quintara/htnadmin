import { useState } from 'react'
import { useParams } from 'react-router'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { SponsorsView } from './components/SponsorsView'
import { PartnersView } from './components/PartnersView'

type View = 'sponsors' | 'partners'

/** Frame 13: sponsors & tiers and partners, behind a segmented control. Board only (gated by the route). */
export function SponsorsPage() {
  const { eventId = '' } = useParams()
  const [view, setView] = useState<View>('sponsors')

  return (
    <div className="flex flex-col gap-5">
      <SegmentedControl
        aria-label="Sponsors or partners"
        value={view}
        onChange={setView}
        options={[
          { value: 'sponsors', label: 'Sponsors' },
          { value: 'partners', label: 'Partners' },
        ]}
      />
      {view === 'sponsors' ? <SponsorsView eventId={eventId} /> : <PartnersView eventId={eventId} />}
    </div>
  )
}
