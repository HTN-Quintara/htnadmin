import { Card, CardHeader } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { formatPeso } from '@/lib/format'
import type { LeftoverVariant } from '../schemas'

/** Leftover inventory tab (canvas PopUps.dc.html): stock moved out of its event, shared across pop-ups. */
export function LeftoverInventoryTable({ variants }: { variants: LeftoverVariant[] }) {
  if (variants.length === 0) {
    return (
      <Card>
        <EmptyState
          title="No leftover stock yet"
          description='Move unsold merch here from an event’s Merch page ("Move leftovers to store") to allocate it to a pop-up.'
        />
      </Card>
    )
  }

  return (
    <Card>
      <CardHeader title={`Leftover inventory · ${variants.reduce((n, v) => n + v.stockOnHand, 0)} pcs`} />
      <Table minWidth={720}>
        <THead>
          <Tr>
            <Th>Item</Th>
            <Th>From</Th>
            <Th className="text-right">On hand</Th>
            <Th className="text-right">At a pop-up</Th>
            <Th className="text-right">Available</Th>
            <Th className="text-right">Price</Th>
          </Tr>
        </THead>
        <TBody>
          {variants.map((v) => (
            <Tr key={v.variantId}>
              <Td className="font-medium text-gray-900 dark:text-gray-50">
                {v.itemName} ({v.variantLabel})
              </Td>
              <Td>{v.fromEventName || '—'}</Td>
              <Td numeric>{v.stockOnHand}</Td>
              <Td numeric>{v.reserved}</Td>
              <Td numeric className={v.available === 0 ? 'font-semibold text-red-700 dark:text-red-400' : undefined}>
                {v.available}
              </Td>
              <Td numeric>{formatPeso(v.price)}</Td>
            </Tr>
          ))}
        </TBody>
      </Table>
    </Card>
  )
}
