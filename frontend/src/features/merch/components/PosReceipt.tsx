import { Button } from '@/components/ui/Button'
import { CheckIcon } from '@/components/ui/icons'
import { formatPeso } from '@/lib/format'
import type { MerchSale } from '../schemas'
import { paymentMethodLabel } from './labels'

/** Post-sale success state (canvas frame 21). */
export function PosReceipt({ sale, onNewSale }: { sale: MerchSale; onNewSale: () => void }) {
  return (
    <div role="status" className="flex flex-col items-center gap-4 px-4 py-10 text-center">
      <span className="flex size-14 items-center justify-center rounded-full bg-green-100 text-green-700 dark:bg-green-500/10 dark:text-green-400">
        <CheckIcon size={28} />
      </span>
      <div className="flex flex-col gap-1">
        <p className="text-lg font-semibold text-gray-900 dark:text-gray-50">Sale recorded</p>
        <p className="text-sm text-gray-600 dark:text-gray-400">
          {formatPeso(sale.total)} from {sale.buyerNameSnapshot} via {paymentMethodLabel(sale.method)}.
        </p>
      </div>
      <Button size="lg" onClick={onNewSale}>
        New sale
      </Button>
    </div>
  )
}
