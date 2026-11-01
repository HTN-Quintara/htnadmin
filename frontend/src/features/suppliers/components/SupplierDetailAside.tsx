import { useState } from 'react'
import { Link } from 'react-router'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Drawer } from '@/components/ui/Drawer'
import { Skeleton } from '@/components/ui/Spinner'
import { formatPeso } from '@/lib/format'
import { useSupplier } from '../api'
import { categoryLabel, hireStatusColor, hireStatusLabel } from './labels'
import { DocumentsPanel } from './DocumentsPanel'
import { SupplierFormDialog } from './SupplierFormDialog'
import type { PreviewRole } from './SupplierTable'

type Props = { supplierId: string | null; onClose: () => void; canManage: boolean; previewRole: PreviewRole }

/** Frame 18b: supplier detail — issues-OR badge, documents, conditional TIN/bank, per-event hire history. */
export function SupplierDetailAside({ supplierId, onClose, canManage, previewRole }: Props) {
  const query = useSupplier(supplierId)
  const [edit, setEdit] = useState(false)

  const open = supplierId !== null
  const supplier = query.data?.supplier
  const documents = query.data?.documents ?? []
  const hires = query.data?.hires ?? []
  // The preview toggle is purely local: a board caller's response always carries boardOnly when present, this just
  // decides whether this aside renders it, so a board user can see what a committee lead sees.
  const showMoney = previewRole === 'Board'

  return (
    <Drawer open={open} onClose={onClose} label="Supplier details">
      {query.isPending || !supplier ? (
        <div className="flex flex-col gap-3" role="status" aria-label="Loading supplier">
          <Skeleton className="h-6 w-48" />
          <Skeleton className="h-40 w-full" />
        </div>
      ) : (
        <div className="flex flex-col gap-5">
          <div className="flex items-start justify-between gap-3">
            <div>
              <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-50">{supplier.name}</h2>
              <div className="mt-1 flex items-center gap-2">
                <Badge color="brand">{categoryLabel(supplier.category)}</Badge>
                {supplier.issuesOr ? <Badge color="green">Issues OR</Badge> : <Badge color="gray">No OR</Badge>}
                {!supplier.isActive ? <Badge color="gray">Deactivated</Badge> : null}
              </div>
            </div>
            {canManage ? <Button size="sm" variant="secondary" onClick={() => setEdit(true)}>Edit</Button> : null}
          </div>

          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
            {supplier.contactName ? (
              <>
                <dt className="text-gray-500 dark:text-gray-400">Contact</dt>
                <dd>
                  {supplier.contactName}
                  {supplier.contactEmail ? `, ${supplier.contactEmail}` : ''}
                  {supplier.contactPhone ? ` · ${supplier.contactPhone}` : ''}
                </dd>
              </>
            ) : null}
            {supplier.boardOnly && showMoney ? (
              <>
                <dt className="text-gray-500 dark:text-gray-400">TIN</dt>
                <dd>{supplier.boardOnly.tin ?? <span className="text-gray-400">—</span>}</dd>
                <dt className="text-gray-500 dark:text-gray-400">Bank details</dt>
                <dd className="whitespace-pre-wrap">{supplier.boardOnly.bankDetails ?? <span className="text-gray-400">—</span>}</dd>
              </>
            ) : null}
            {supplier.notes ? (
              <>
                <dt className="text-gray-500 dark:text-gray-400">Notes</dt>
                <dd>{supplier.notes}</dd>
              </>
            ) : null}
          </dl>

          <DocumentsPanel supplierId={supplier.id} documents={documents} canManage={canManage} />

          <section className="flex flex-col gap-3">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-50">Hire history</h3>
              {canManage ? (
                <Link to="/events" className="text-xs font-medium text-brand-600 hover:underline dark:text-brand-300">
                  Hire for an event
                </Link>
              ) : null}
            </div>
            {hires.length === 0 ? (
              <p className="text-sm text-gray-500 dark:text-gray-400">Not hired for any event yet.</p>
            ) : (
              <ul className="flex flex-col gap-2">
                {hires.map((h) => (
                  <li key={h.id} className="flex flex-col gap-1 rounded-md border border-gray-200 p-2.5 text-sm dark:border-white/10">
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-medium text-gray-900 dark:text-gray-50">{h.ownerCommitteeName}</span>
                      <Badge color={hireStatusColor(h.status)}>{hireStatusLabel(h.status)}</Badge>
                    </div>
                    <span className="text-xs text-gray-500 dark:text-gray-400">
                      {h.categoryName}
                      {h.isVoided ? ' · Voided' : ''}
                    </span>
                    {h.money && showMoney ? (
                      <span className="text-xs text-gray-600 dark:text-gray-300">
                        Contract {h.money.contractAmount != null ? formatPeso(h.money.contractAmount) : '—'} · Paid{' '}
                        {h.money.paidAmount != null ? formatPeso(h.money.paidAmount) : '—'}
                      </span>
                    ) : null}
                    {h.ratingCount > 0 ? (
                      <span className="text-xs text-gray-600 dark:text-gray-300">
                        {h.averageStars?.toFixed(1)}★ average ({h.ratingCount} rating{h.ratingCount === 1 ? '' : 's'})
                      </span>
                    ) : null}
                    <Link to={`/events/${h.eventId}/suppliers`} className="text-xs font-medium text-brand-600 hover:underline dark:text-brand-300">
                      Open event
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <SupplierFormDialog open={edit} onClose={() => setEdit(false)} supplier={supplier} />
        </div>
      )}
    </Drawer>
  )
}
