import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Checkbox } from '@/components/ui/Input'
import { Dialog } from '@/components/ui/Dialog'
import { useMoveToStoreMutation } from '../api'
import type { MerchItem } from '../schemas'

/** Event Merch page's "Move leftovers to store" action (Phase 8C): picks variants to unlink into the shared pool. */
export function MoveToStoreDialog({ eventId, items, onClose }: { eventId: string; items: MerchItem[]; onClose: () => void }) {
  const move = useMoveToStoreMutation(eventId)
  const [selected, setSelected] = useState<Set<string>>(new Set())

  const activeItems = items.filter((i) => i.isActive && i.eventId === eventId)

  function toggle(variantId: string) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(variantId)) next.delete(variantId)
      else next.add(variantId)
      return next
    })
  }

  function onSubmit() {
    if (move.isPending || selected.size === 0) return
    move.mutate([...selected], { onSuccess: onClose })
  }

  return (
    <Dialog
      open
      onClose={onClose}
      title="Move leftovers to store"
      size="lg"
      description="Pick the variants to unlink from this event. Their remaining stock joins the shared leftover pool for pop-up stores."
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={onSubmit} disabled={selected.size === 0} loading={move.isPending}>
            Move {selected.size > 0 ? `${selected.size} ` : ''}to the store
          </Button>
        </>
      }
    >
      {move.isError ? <Alert tone="red" live="alert">{errorMessage(move.error)}</Alert> : null}
      {activeItems.length === 0 ? (
        <p className="text-sm text-gray-600 dark:text-gray-400">No active items on this event to move.</p>
      ) : (
        <ul className="flex flex-col divide-y divide-gray-100 dark:divide-white/10">
          {activeItems.flatMap((item) =>
            item.variants
              .filter((v) => v.isActive)
              .map((v) => (
                <li key={v.id} className="flex min-h-11 items-center gap-3 py-2">
                  <label className="flex min-h-11 flex-1 items-center gap-2.5 text-sm text-gray-900 dark:text-gray-50">
                    <Checkbox checked={selected.has(v.id)} onChange={() => toggle(v.id)} />
                    {item.name} ({v.label}) — {v.stockOnHand} left
                  </label>
                </li>
              )),
          )}
        </ul>
      )}
    </Dialog>
  )
}
