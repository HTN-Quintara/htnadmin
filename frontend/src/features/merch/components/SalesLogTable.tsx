import { useAuth } from '@/app/auth/auth-context'
import { Badge } from '@/components/ui/Badge'
import { Card, CardHeader } from '@/components/ui/Card'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { buttonClass } from '@/components/ui/button-styles'
import { formatPeso } from '@/lib/format'
import { exportMerchSalesCsvUrl } from '../api'
import type { MerchSale } from '../schemas'
import { levelBadgeColor, paymentMethodLabel } from './labels'

const timeFmt = new Intl.DateTimeFormat('en-PH', { hour: 'numeric', minute: '2-digit' })

function cashierLabel(cashierId: string, myId: string | undefined): string {
  if (cashierId === myId) return 'You'
  return `Cashier ${cashierId.slice(0, 6)}`
}

/** "Sales · who bought what" (canvas frame 20, full-width table below). */
export function SalesLogTable({ eventId, sales, canManage }: { eventId: string; sales: MerchSale[]; canManage: boolean }) {
  const { me } = useAuth()
  return (
    <Card>
      <CardHeader
        title="Sales · who bought what"
        action={
          canManage ? (
            <a href={exportMerchSalesCsvUrl(eventId)} download className={buttonClass('secondary', 'sm')}>
              Export CSV
            </a>
          ) : undefined
        }
      />
      <Table minWidth={820}>
        <THead>
          <Tr>
            <Th>Time</Th>
            <Th>Buyer</Th>
            <Th>Items</Th>
            <Th>Discount</Th>
            <Th className="text-right">Total</Th>
            <Th>Paid via</Th>
            <Th>Cashier</Th>
          </Tr>
        </THead>
        <TBody>
          {sales.map((sale) => (
            <Tr key={sale.id}>
              <Td>{timeFmt.format(new Date(sale.soldAt))}</Td>
              <Td>
                <span className="flex flex-col gap-0.5">
                  <span className="font-medium text-gray-900 dark:text-gray-50">{sale.buyerNameSnapshot}</span>
                  <Badge color={levelBadgeColor(sale.buyerLevelSnapshot ?? 'Attendee')} className="w-fit">
                    {sale.buyerLevelSnapshot ?? 'Attendee'}
                  </Badge>
                </span>
              </Td>
              <Td>{sale.lines.map((l) => `${l.itemName} (${l.variantLabel}) ×${l.qty}`).join(', ')}</Td>
              <Td>
                {sale.discountAmount > 0 ? (
                  <span className="font-medium text-green-700 dark:text-green-400">
                    −{formatPeso(sale.discountAmount)} ({sale.discountPct}%)
                  </span>
                ) : (
                  <span className="text-gray-500 dark:text-gray-500">—</span>
                )}
              </Td>
              <Td numeric>{formatPeso(sale.total)}</Td>
              <Td>{paymentMethodLabel(sale.method)}</Td>
              <Td>{cashierLabel(sale.cashierId, me?.id)}</Td>
            </Tr>
          ))}
        </TBody>
      </Table>
      {sales.length === 0 ? <p className="px-5 py-6 text-sm text-gray-600 dark:text-gray-400">No sales yet for this event.</p> : null}
    </Card>
  )
}
