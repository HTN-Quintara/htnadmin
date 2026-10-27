import { useMemo, useState } from 'react'
import { useDirectoryOrgs } from '@/features/directory/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Checkbox, Input } from '@/components/ui/Input'
import { Dialog } from '@/components/ui/Dialog'
import { EmptyState } from '@/components/ui/EmptyState'
import { Skeleton } from '@/components/ui/Spinner'
import { useToast } from '@/components/ui/toast-context'
import { useCreateInvitationsMutation } from '../api'

type Props = {
  eventId: string
  open: boolean
  onClose: () => void
  /** Directory org ids already picked for this event — excluded from the list. */
  alreadyInvitedOrgIds: string[]
}

/** Step 1, "Pick list": search the directory and add organizations not yet invited to this event. */
export function PickOrgsDialog({ eventId, open, onClose, alreadyInvitedOrgIds }: Props) {
  const query = useDirectoryOrgs()
  const create = useCreateInvitationsMutation(eventId)
  const { toast } = useToast()
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<string[]>([])

  const candidates = useMemo(() => {
    const alreadyInvited = new Set(alreadyInvitedOrgIds)
    const orgs = (query.data ?? []).filter((o) => o.isActive && !alreadyInvited.has(o.id))
    const term = search.trim().toLowerCase()
    return term ? orgs.filter((o) => o.name.toLowerCase().includes(term)) : orgs
  }, [query.data, search, alreadyInvitedOrgIds])

  function toggle(id: string) {
    setSelected((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]))
  }

  function reset() {
    setSearch('')
    setSelected([])
  }

  function onSubmit() {
    if (selected.length === 0 || create.isPending) return
    create.mutate(selected, {
      onSuccess: (result) => {
        toast({
          tone: 'success',
          title: `${result.created.length} organization${result.created.length === 1 ? '' : 's'} added.`,
          description: result.skipped.length > 0 ? `${result.skipped.length} already invited and skipped.` : undefined,
        })
        reset()
        onClose()
      },
      onError: (e) => toast({ tone: 'error', title: 'Could not add organizations.', description: errorMessage(e) }),
    })
  }

  return (
    <Dialog
      open={open}
      onClose={() => { reset(); onClose() }}
      title="Pick organizations"
      description="Search the directory and pick who to invite to this event."
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={() => { reset(); onClose() }}>
            Cancel
          </Button>
          <Button onClick={onSubmit} loading={create.isPending} disabled={selected.length === 0}>
            Add {selected.length > 0 ? selected.length : ''} organization{selected.length === 1 ? '' : 's'}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-3">
        <label className="flex flex-col gap-1.5">
          <span className="sr-only">Search organizations</span>
          <Input placeholder="Search organizations…" value={search} onChange={(e) => setSearch(e.target.value)} />
        </label>

        {query.isPending ? (
          <div role="status" aria-label="Loading organizations">
            <Skeleton className="h-48 w-full" />
          </div>
        ) : query.isError ? (
          <Alert tone="red" action={<Button size="sm" variant="secondary" onClick={() => query.refetch()}>Try again</Button>}>
            Could not load the directory. {errorMessage(query.error)}
          </Alert>
        ) : candidates.length === 0 ? (
          <EmptyState title="No organizations to pick" description="Every active organization matching this search has already been invited." />
        ) : (
          <ul className="flex max-h-80 flex-col gap-1 overflow-y-auto">
            {candidates.map((org) => (
              <li key={org.id}>
                <label className="flex min-h-11 cursor-pointer items-center gap-3 rounded-md px-2 hover:bg-gray-50 dark:hover:bg-white/5">
                  <Checkbox checked={selected.includes(org.id)} onChange={() => toggle(org.id)} />
                  <span className="flex flex-col">
                    <span className="text-sm font-medium text-gray-900 dark:text-gray-50">{org.name}</span>
                    <span className="text-xs text-gray-500 dark:text-gray-400">{org.kind}</span>
                  </span>
                </label>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Dialog>
  )
}
