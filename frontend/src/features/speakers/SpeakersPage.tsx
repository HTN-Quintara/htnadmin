import { useState } from 'react'
import { useParams } from 'react-router'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardBody } from '@/components/ui/Card'
import { Skeleton } from '@/components/ui/Spinner'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { useToast } from '@/components/ui/toast-context'
import { useSendUnsentMutation, useSpeakersListQuery } from './api'
import { LinkStateBadge } from './components/badges'
import { SpeakerDrawer } from './components/SpeakerDrawer'

/** Frame 22: speakers, confirmation links and (board/Programs-lead only) travel. */
export function SpeakersPage() {
  const { eventId = '' } = useParams()
  const query = useSpeakersListQuery(eventId)
  const sendUnsent = useSendUnsentMutation(eventId)
  const { toast } = useToast()
  const [openId, setOpenId] = useState<string | null>(null)

  if (query.isPending) return <div role="status" aria-label="Loading speakers"><Skeleton className="h-64 w-full" /></div>
  if (query.isError) {
    return (
      <Alert tone="red" action={<Button size="sm" variant="secondary" onClick={() => query.refetch()}>Try again</Button>}>
        Could not load speakers. {errorMessage(query.error)}
      </Alert>
    )
  }

  const page = query.data
  const unsentCount = page.speakers.filter((sp) => sp.linkState === 'Not sent' || sp.linkState === 'Expired' || sp.linkState === 'Revoked').length

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <span className="text-sm text-gray-600 dark:text-gray-300">
          Travel for this event: <strong className="font-semibold text-gray-900 dark:text-gray-50">{page.travelMode}</strong>
        </span>
        {unsentCount > 0 && (
          <Button
            size="sm"
            loading={sendUnsent.isPending}
            onClick={() => sendUnsent.mutate(undefined, {
              onSuccess: (r) => toast({ tone: 'success', title: `Sent ${r.sent} link${r.sent === 1 ? '' : 's'}.` }),
              onError: (e) => toast({ tone: 'error', title: errorMessage(e) }),
            })}
          >
            Send links to {unsentCount} not yet sent
          </Button>
        )}
      </div>

      <section aria-label="Confirmation progress" className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {page.kpis.map((k) => (
          <Card key={k.label}><CardBody><div className="flex flex-col gap-1"><span className="text-xs text-gray-500 dark:text-gray-400">{k.label}</span><span className="text-xl font-semibold text-gray-900 dark:text-gray-50">{k.value}</span></div></CardBody></Card>
        ))}
      </section>

      <div className="overflow-x-auto">
        <Table minWidth={820}>
          <THead>
            <Tr>
              <Th>Speaker</Th>
              <Th>Confirmation link</Th>
              <Th>Arrive → depart</Th>
              <Th>Travel</Th>
              <Th>Plus-one</Th>
            </Tr>
          </THead>
          <TBody>
            {page.speakers.map((sp) => (
              <Tr key={sp.id} className="cursor-pointer" onClick={() => setOpenId(sp.id)}>
                <Td>
                  <button type="button" className="flex flex-col text-left">
                    <span className="font-medium text-brand-600 hover:text-brand-700 dark:text-brand-300">{sp.name}</span>
                    <span className="text-xs text-gray-500 dark:text-gray-400">{sp.session}</span>
                  </button>
                </Td>
                <Td><LinkStateBadge state={sp.linkState} sub={sp.linkStateSub} /></Td>
                <Td>{sp.dates}</Td>
                <Td>{sp.travel}</Td>
                <Td>{sp.plusOne}</Td>
              </Tr>
            ))}
          </TBody>
        </Table>
      </div>

      <SpeakerDrawer eventId={eventId} speakerId={openId} onClose={() => setOpenId(null)} />
    </div>
  )
}
