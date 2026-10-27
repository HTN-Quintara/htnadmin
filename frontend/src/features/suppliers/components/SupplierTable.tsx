import { useMemo, useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { EmptyState } from '@/components/ui/EmptyState'
import { FilterChip } from '@/components/ui/FilterChip'
import { SegmentedControl } from '@/components/ui/SegmentedControl'
import { TruckIcon } from '@/components/ui/icons'
import { Skeleton } from '@/components/ui/Spinner'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { useSuppliers } from '../api'
import type { SupplierCategory } from '../schemas'
import { categoryLabel } from './labels'

type CategoryFilter = 'All' | SupplierCategory
const categoryFilters: CategoryFilter[] = ['All', 'Venue', 'Catering', 'Printing', 'TechAV', 'Merch', 'Transport']

export type PreviewRole = 'Board' | 'Lead'

type Props = {
  onOpen: (id: string) => void
  onAdd: () => void
  canManage: boolean
  previewRole: PreviewRole
  onPreviewRoleChange: (role: PreviewRole) => void
}

/**
 * Frame 18b: the global supplier table. `previewRole` is a purely local display toggle for board users to preview
 * what a committee lead sees — it never changes which API call is made (a board caller's response always carries
 * the real `boardOnly` money fields); it only decides whether this table renders them.
 */
export function SupplierTable({ onOpen, onAdd, canManage, previewRole, onPreviewRoleChange }: Props) {
  const [filter, setFilter] = useState<CategoryFilter>('All')
  const query = useSuppliers(filter === 'All' ? undefined : filter)
  const suppliers = useMemo(() => query.data ?? [], [query.data])

  if (query.isPending) {
    return (
      <div role="status" aria-label="Loading suppliers" className="flex flex-col gap-3">
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    )
  }
  if (query.isError) {
    return (
      <Alert tone="red" action={<Button size="sm" variant="secondary" onClick={() => query.refetch()}>Try again</Button>}>
        Could not load suppliers. {errorMessage(query.error)}
      </Alert>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap gap-2">
          {categoryFilters.map((f) => (
            <FilterChip key={f} selected={filter === f} onClick={() => setFilter(f)}>
              {f === 'All' ? 'All' : categoryLabel(f)}
            </FilterChip>
          ))}
        </div>
        <div className="flex flex-wrap items-center gap-3">
          {canManage ? (
            <SegmentedControl
              aria-label="Preview money fields as"
              options={[
                { value: 'Board', label: 'Preview as Board/CFO' },
                { value: 'Lead', label: 'Committee lead' },
              ]}
              value={previewRole}
              onChange={onPreviewRoleChange}
            />
          ) : null}
          {canManage ? <Button size="sm" onClick={onAdd}>+ Supplier</Button> : null}
        </div>
      </div>

      {suppliers.length === 0 ? (
        <EmptyState
          icon={<TruckIcon />}
          title={filter === 'All' ? 'No suppliers yet' : 'No suppliers match this filter'}
          description={filter === 'All' ? 'Add a venue, caterer, printer, tech/AV, merch or transport supplier to start tracking hires.' : 'Try another category.'}
          action={canManage && filter === 'All' ? <Button onClick={onAdd}>+ Supplier</Button> : undefined}
        />
      ) : (
        <Table minWidth={760}>
          <THead>
            <Tr>
              <Th>Supplier</Th>
              <Th>Contact</Th>
              <Th>Rating &amp; documents</Th>
              <Th>Used in</Th>
            </Tr>
          </THead>
          <TBody>
            {suppliers.map((s) => (
              <Tr key={s.id} className="cursor-pointer" onClick={() => onOpen(s.id)}>
                <Td>
                  <div className="flex flex-col">
                    <button type="button" className="text-left font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300">
                      {s.name}
                    </button>
                    <span className="text-xs text-gray-500 dark:text-gray-400">{categoryLabel(s.category)}</span>
                  </div>
                </Td>
                <Td>
                  {s.contactName ? (
                    <div className="flex flex-col">
                      <span>{s.contactName}</span>
                      {s.contactEmail ? <span className="text-xs text-gray-500 dark:text-gray-400">{s.contactEmail}</span> : null}
                    </div>
                  ) : (
                    <span className="text-gray-400">—</span>
                  )}
                </Td>
                {/* The list endpoint carries only the fields above; ratings, documents and hire history are on the
                    detail endpoint, so this column links to the aside rather than showing counts (avoids an N+1
                    request per row). See the final report's deviations note. */}
                <Td>
                  <button
                    type="button"
                    className="text-xs font-medium text-brand-600 hover:underline dark:text-brand-300"
                    onClick={(e) => { e.stopPropagation(); onOpen(s.id) }}
                  >
                    View details
                  </button>
                </Td>
                <Td>{!s.isActive ? <Badge color="gray">Deactivated</Badge> : <span className="text-gray-400">Active</span>}</Td>
              </Tr>
            ))}
          </TBody>
        </Table>
      )}
    </div>
  )
}
