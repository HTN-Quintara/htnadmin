import { useMemo, useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { FilterChip } from '@/components/ui/FilterChip'
import { AlertIcon, BuildingIcon } from '@/components/ui/icons'
import { Skeleton } from '@/components/ui/Spinner'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { useDirectoryOrgs, type DirectoryFilters } from '../api'
import { orgKindLabel } from './labels'

type Filter = 'All' | 'Government' | 'Companies' | 'Universities' | 'Needs check'
const filters: Filter[] = ['All', 'Government', 'Companies', 'Universities', 'Needs check']

function toApiFilters(f: Filter): DirectoryFilters {
  const byFilter: Record<Filter, DirectoryFilters> = {
    All: {},
    Government: { kind: 'Government' },
    Companies: { kind: 'Company' },
    Universities: { kind: 'University' },
    'Needs check': { needsCheck: true },
  }
  return byFilter[f]
}

type Props = {
  onOpen: (id: string) => void
  onAdd: () => void
  canManage: boolean
}

/** Frame 18: the global directory table, filter chips and the 90-day needs-check banner. */
export function OrgTable({ onOpen, onAdd, canManage }: Props) {
  const [filter, setFilter] = useState<Filter>('All')
  const query = useDirectoryOrgs(toApiFilters(filter))

  // A second, unfiltered-by-needsCheck count so the banner can show even while viewing another chip.
  const needsCheckCount = useDirectoryOrgs({ needsCheck: true }).data?.length ?? 0

  const orgs = useMemo(() => query.data ?? [], [query.data])

  if (query.isPending) {
    return (
      <div role="status" aria-label="Loading directory" className="flex flex-col gap-3">
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    )
  }
  if (query.isError) {
    return (
      <Alert tone="red" action={<Button size="sm" variant="secondary" onClick={() => query.refetch()}>Try again</Button>}>
        Could not load the directory. {errorMessage(query.error)}
      </Alert>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap gap-2">
          {filters.map((f) => (
            <FilterChip key={f} selected={filter === f} onClick={() => setFilter(f)}>
              {f}
            </FilterChip>
          ))}
        </div>
        {canManage ? <Button size="sm" onClick={onAdd}>+ Organization</Button> : null}
      </div>

      {needsCheckCount > 0 && filter !== 'Needs check' ? (
        <Alert tone="yellow" live="status" action={<Button size="sm" variant="secondary" onClick={() => setFilter('Needs check')}>Review</Button>}>
          <span className="flex items-center gap-2">
            <AlertIcon size={16} />
            {needsCheckCount} organization{needsCheckCount === 1 ? '' : 's'} {needsCheckCount === 1 ? "hasn't" : "haven't"} been checked in the last 90 days.
          </span>
        </Alert>
      ) : null}

      {orgs.length === 0 ? (
        <EmptyState
          icon={<BuildingIcon />}
          title={filter === 'All' ? 'No organizations yet' : 'No organizations match this filter'}
          description={filter === 'All' ? 'Add a government agency, company, university or non-profit to start tracking contacts.' : 'Try another filter.'}
          action={canManage && filter === 'All' ? <Button onClick={onAdd}>+ Organization</Button> : undefined}
        />
      ) : (
        <Table minWidth={760}>
          <THead>
            <Tr>
              <Th>Organization</Th>
              <Th>Addressee</Th>
              <Th>Contact checked</Th>
              <Th>History</Th>
            </Tr>
          </THead>
          <TBody>
            {orgs.map((o) => (
              <Tr key={o.id} className="cursor-pointer" onClick={() => onOpen(o.id)}>
                <Td>
                  <div className="flex flex-col">
                    <button type="button" className="text-left font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300">
                      {o.name}
                    </button>
                    <span className="text-xs text-gray-500 dark:text-gray-400">{orgKindLabel(o.kind)}</span>
                  </div>
                </Td>
                <Td>{o.addresseeSalutation ?? <span className="text-gray-400">—</span>}</Td>
                <Td>
                  {o.needsCheck ? (
                    <Badge color="yellow">Needs check</Badge>
                  ) : (
                    <Badge color="green">Checked</Badge>
                  )}
                </Td>
                <Td>
                  <button
                    type="button"
                    className="text-xs font-medium text-brand-600 hover:underline dark:text-brand-300"
                    onClick={(e) => { e.stopPropagation(); onOpen(o.id) }}
                  >
                    View history
                  </button>
                </Td>
              </Tr>
            ))}
          </TBody>
        </Table>
      )}
    </div>
  )
}
