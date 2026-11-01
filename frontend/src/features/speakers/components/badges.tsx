import { Badge } from '@/components/ui/Badge'

const colors: Record<string, 'gray' | 'blue' | 'brand' | 'green' | 'red'> = {
  'Not sent': 'gray',
  Sent: 'blue',
  Opened: 'brand',
  Submitted: 'green',
  Expired: 'red',
  Revoked: 'red',
}

export function LinkStateBadge({ state, sub }: { state: string; sub?: string | null }) {
  return (
    <div className="flex flex-col gap-0.5">
      <Badge color={colors[state] ?? 'gray'}>{state}</Badge>
      {sub && <span className="text-xs text-gray-500 dark:text-gray-400">{sub}</span>}
    </div>
  )
}
