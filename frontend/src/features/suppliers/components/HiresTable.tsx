import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/toast-context'
import { formatPeso } from '@/lib/format'
import { useDeleteHireMutation, useUpdateHireMutation, useVoidHireMutation } from '../api'
import type { EventSupplier } from '../schemas'
import { hireStatusColor, hireStatusLabel, nextHireStatus } from './labels'
import { HireMoneyDialog } from './HireMoneyDialog'
import { ReasonDialog } from './ReasonDialog'

type Props = { eventId: string; hires: EventSupplier[]; canManage: boolean }

/** Frame 25-adjacent: the per-event hires list. Status only ever offers the one valid next step — never a free-choice dropdown. */
export function HiresTable({ eventId, hires, canManage }: Props) {
  const advance = useUpdateHireMutation(eventId)
  const voidHire = useVoidHireMutation(eventId)
  const del = useDeleteHireMutation(eventId)
  const { toast } = useToast()
  const [editing, setEditing] = useState<EventSupplier | null>(null)
  const [voiding, setVoiding] = useState<EventSupplier | null>(null)
  const [deleting, setDeleting] = useState<EventSupplier | null>(null)

  function onAdvance(h: EventSupplier) {
    const next = nextHireStatus(h.status)
    if (!next || advance.isPending) return
    advance.mutate(
      { hireId: h.id, body: { contractAmount: h.money?.contractAmount ?? null, paidAmount: h.money?.paidAmount ?? null, nextPaymentAmount: h.money?.nextPaymentAmount ?? null, nextPaymentDue: h.money?.nextPaymentDue ?? null, note: h.note, status: next } },
      {
        onSuccess: () => toast({ tone: 'success', title: `Moved to ${hireStatusLabel(next)}.` }),
        onError: (e) => toast({ tone: 'error', title: 'Could not advance the hire.', description: errorMessage(e) }),
      },
    )
  }

  return (
    <>
      <Table minWidth={760}>
        <THead>
          <Tr>
            <Th>Supplier</Th>
            <Th>Category</Th>
            <Th>Committee</Th>
            <Th>Status</Th>
            <Th>Contract</Th>
            {canManage ? <Th>Actions</Th> : null}
          </Tr>
        </THead>
        <TBody>
          {hires.map((h) => {
            const next = nextHireStatus(h.status)
            return (
              <Tr key={h.id}>
                <Td>
                  <div className="flex flex-col">
                    <span className="font-medium text-gray-900 dark:text-gray-50">{h.supplierName}</span>
                    {h.isVoided ? <span className="text-xs text-gray-500 dark:text-gray-400">Voided</span> : null}
                  </div>
                </Td>
                <Td>{h.categoryName}</Td>
                <Td>{h.ownerCommitteeName}</Td>
                <Td><Badge color={hireStatusColor(h.status)}>{hireStatusLabel(h.status)}</Badge></Td>
                <Td numeric>{h.money?.contractAmount != null ? formatPeso(h.money.contractAmount) : '—'}</Td>
                {canManage ? (
                  <Td>
                    {h.isVoided ? null : (
                      <div className="flex flex-wrap gap-2">
                        <Button size="sm" variant="secondary" onClick={() => setEditing(h)}>Edit</Button>
                        {next ? (
                          <Button size="sm" variant="secondary" loading={advance.isPending} onClick={() => onAdvance(h)}>
                            Move to {hireStatusLabel(next)}
                          </Button>
                        ) : null}
                        {h.status === 'Quotation' ? (
                          <Button size="sm" variant="destructive" onClick={() => setDeleting(h)}>Delete</Button>
                        ) : (
                          <Button size="sm" variant="destructive" onClick={() => setVoiding(h)}>Void</Button>
                        )}
                      </div>
                    )}
                  </Td>
                ) : null}
              </Tr>
            )
          })}
        </TBody>
      </Table>

      <HireMoneyDialog open={editing !== null} onClose={() => setEditing(null)} eventId={eventId} hire={editing} />

      <ReasonDialog
        open={voiding !== null}
        onClose={() => setVoiding(null)}
        title="Void hire"
        description="The booking falls through: void this hire with a reason, then create a new one if needed."
        confirmLabel="Void hire"
        pending={voidHire.isPending}
        onConfirm={(reason) => {
          if (!voiding) return
          voidHire.mutate(
            { hireId: voiding.id, reason },
            {
              onSuccess: () => { toast({ tone: 'success', title: 'Hire voided.' }); setVoiding(null) },
              onError: (e) => toast({ tone: 'error', title: 'Could not void the hire.', description: errorMessage(e) }),
            },
          )
        }}
      />

      <Dialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        role="alertdialog"
        size="sm"
        title="Delete hire?"
        description="This quotation hasn't moved forward yet, so it can be deleted outright instead of voided."
        footer={
          <>
            <Button variant="secondary" onClick={() => setDeleting(null)}>Cancel</Button>
            <Button
              variant="destructive"
              loading={del.isPending}
              onClick={() => {
                if (!deleting) return
                del.mutate(deleting.id, {
                  onSuccess: () => { toast({ tone: 'success', title: 'Hire deleted.' }); setDeleting(null) },
                  onError: (e) => toast({ tone: 'error', title: 'Could not delete the hire.', description: errorMessage(e) }),
                })
              }}
            >
              Delete hire
            </Button>
          </>
        }
      />
    </>
  )
}
