import { FilterChip } from '@/components/ui/FilterChip'
import { Spinner } from '@/components/ui/Spinner'
import { useSetPopUpsAllowedMutation } from '@/features/committees/api'
import type { EligibleCommittee } from '../api'

/**
 * One toggle chip per event committee (canvas: "Who can create pop-ups"). Committees are event-scoped in this
 * codebase (`PUT /api/events/{eventId}/committees/{committeeId}/pop-ups-allowed`), unlike the mock's flat
 * committee list — see MerchStorePage's report for how this was resolved.
 */
export function CommitteePopUpToggle({ committee }: { committee: EligibleCommittee }) {
  const toggle = useSetPopUpsAllowedMutation(committee.eventId)
  const allowed = committee.popUpsAllowed

  return (
    <FilterChip
      selected={allowed}
      disabled={toggle.isPending}
      aria-label={`${allowed ? 'Disallow' : 'Allow'} ${committee.name} (${committee.eventName}) to run pop-ups`}
      onClick={() => toggle.mutate({ committeeId: committee.committeeId, allowed: !allowed })}
      className="min-h-11"
    >
      {toggle.isPending ? <Spinner /> : null}
      {committee.name} · {committee.eventName}
    </FilterChip>
  )
}
