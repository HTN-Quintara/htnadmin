import { useState } from 'react'
import { errorMessage } from '@/app/errors'
import { Button } from '@/components/ui/Button'
import { Dialog } from '@/components/ui/Dialog'
import { Input } from '@/components/ui/Input'
import { useToast } from '@/components/ui/toast-context'
import { useDeactivateRoomMutation, useSaveRoomMutation } from '../api'
import type { Room } from '../schemas'

export function RoomsDialog({ eventId, rooms, open, onClose }: { eventId: string; rooms: Room[]; open: boolean; onClose: () => void }) {
  const save = useSaveRoomMutation(eventId)
  const deactivate = useDeactivateRoomMutation(eventId)
  const { toast } = useToast()
  const [name, setName] = useState('')
  const [color, setColor] = useState('#B71C1C')
  const [error, setError] = useState<string | null>(null)

  function add() {
    if (save.isPending) return
    if (!name.trim()) { setError('Give the room a name.'); return }
    save.mutate({ body: { name: name.trim(), colorHex: color, sortOrder: rooms.length } }, {
      onSuccess: () => { setName(''); setColor('#B71C1C'); toast({ tone: 'success', title: 'Room added.' }) },
      onError: (e) => setError(errorMessage(e)),
    })
  }

  return (
    <Dialog open={open} onClose={onClose} title="Rooms" footer={<Button onClick={onClose}>Done</Button>}>
      <div className="flex flex-col gap-4">
        <ul className="flex flex-col gap-2">
          {rooms.filter((r) => r.isActive).map((r) => (
            <li key={r.id} className="flex items-center gap-2 text-sm">
              <span aria-hidden className="h-3 w-3 rounded-full" style={{ background: r.colorHex }} />
              <span className="flex-1">{r.name}</span>
              <Button size="sm" variant="ghost" onClick={() => deactivate.mutate(r.id, { onError: (e) => toast({ tone: 'error', title: errorMessage(e) }) })}>Remove</Button>
            </li>
          ))}
          {rooms.filter((r) => r.isActive).length === 0 && <li className="text-sm text-gray-500 dark:text-gray-400">No rooms yet.</li>}
        </ul>
        <div className="flex items-end gap-2">
          <label className="flex flex-1 flex-col gap-1 text-sm font-medium text-gray-700 dark:text-gray-200">Name
            <Input value={name} onChange={(e) => { setName(e.target.value); setError(null) }} />
          </label>
          <label className="flex flex-col gap-1 text-sm font-medium text-gray-700 dark:text-gray-200">Colour
            <input type="color" value={color} onChange={(e) => setColor(e.target.value.toUpperCase())} className="h-10 w-14 rounded-md border border-gray-300 dark:border-white/15" aria-label="Room colour" />
          </label>
          <Button loading={save.isPending} onClick={add}>Add</Button>
        </div>
        {error && <span className="text-xs text-red-700 dark:text-red-400">{error}</span>}
      </div>
    </Dialog>
  )
}
