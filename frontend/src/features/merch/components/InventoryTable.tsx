import { formatPeso } from '@/lib/format'
import { Card, CardHeader } from '@/components/ui/Card'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { cn } from '@/lib/cn'
import type { MerchItem, MerchSale } from '../schemas'

/** Per-item sold quantity and revenue, summed from every sale line across the dashboard's sales log. */
function salesByItem(sales: MerchSale[]): Map<string, { sold: number; revenue: number }> {
  const out = new Map<string, { sold: number; revenue: number }>()
  for (const sale of sales) {
    for (const line of sale.lines) {
      const cur = out.get(line.itemId) ?? { sold: 0, revenue: 0 }
      cur.sold += line.qty
      cur.revenue += line.lineTotal
      out.set(line.itemId, cur)
    }
  }
  return out
}

/** Inventory table (canvas frame 20, left column): item, price, stock per variant, sold, revenue. */
export function InventoryTable({ items, sales }: { items: MerchItem[]; sales: MerchSale[] }) {
  const totals = salesByItem(sales)
  return (
    <Card>
      <CardHeader title="Inventory" description="Stock is counted per event; leftovers can be moved to the next event." />
      <Table minWidth={720}>
        <THead>
          <Tr>
            <Th>Item</Th>
            <Th>Price</Th>
            <Th>Stock left by variant</Th>
            <Th className="text-right">Sold</Th>
            <Th className="text-right">Sales</Th>
          </Tr>
        </THead>
        <TBody>
          {items.map((item) => {
            const t = totals.get(item.id) ?? { sold: 0, revenue: 0 }
            return (
              <Tr key={item.id}>
                <Td>
                  <span className="flex items-center gap-2">
                    <span
                      aria-hidden="true"
                      className="size-5 shrink-0 rounded-full ring-1 ring-gray-900/10 dark:ring-white/15"
                      style={{ backgroundColor: item.swatchColor ?? '#9CA3AF' }}
                    />
                    <span className="font-medium text-gray-900 dark:text-gray-50">{item.name}</span>
                    {!item.isActive ? <span className="text-xs text-gray-500">(deactivated)</span> : null}
                  </span>
                </Td>
                <Td numeric>{formatPeso(item.price)}</Td>
                <Td>
                  <span className="flex flex-wrap gap-1.5">
                    {item.variants.map((v) => (
                      <span
                        key={v.id}
                        className={cn(
                          'inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-xs font-medium whitespace-nowrap',
                          v.lowStock
                            ? 'bg-red-50 text-red-700 dark:bg-red-400/10 dark:text-red-300'
                            : 'bg-gray-100 text-gray-700 dark:bg-white/10 dark:text-gray-300',
                        )}
                      >
                        {v.label}: {v.stockOnHand}
                      </span>
                    ))}
                  </span>
                </Td>
                <Td numeric>{t.sold}</Td>
                <Td numeric>{formatPeso(t.revenue)}</Td>
              </Tr>
            )
          })}
        </TBody>
      </Table>
      <p className="px-5 py-3 text-xs text-gray-600 dark:text-gray-400">
        Red = 5 or fewer left. Stock is counted per event; leftovers can be moved to the next event.
      </p>
    </Card>
  )
}
