import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardBody, CardHeader } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Skeleton } from '@/components/ui/Spinner'
import { Stat } from '@/components/ui/Stat'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { WalletIcon } from '@/components/ui/icons'
import { errorMessage } from '@/app/errors'
import { formatPeso, formatPesoSigned } from '@/lib/format'
import { useOverviewQuery, useSweepMutation } from '../api'
import { useToast } from '@/components/ui/toast-context'
import { CashflowChart } from './CashflowChart'

/** Frame 24a: what came in, what went out, what each fund holds. */
export function CashflowView() {
  const query = useOverviewQuery()
  const sweep = useSweepMutation()
  const { toast } = useToast()

  if (query.isPending) {
    return (
      <div className="flex flex-col gap-4" role="status" aria-label="Loading the cashflow…">
        <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(200px,1fr))]">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
        <Skeleton className="h-64" />
      </div>
    )
  }

  if (query.isError) {
    return (
      <Alert
        tone="red"
        live="alert"
        action={
          <Button variant="secondary" size="sm" onClick={() => void query.refetch()}>
            Try again
          </Button>
        }
      >
        Could not load the cashflow. {errorMessage(query.error)}
      </Alert>
    )
  }

  const overview = query.data
  const onSweep = (fundId: string, name: string) => {
    if (sweep.isPending) return
    sweep.mutate(
      { fundId },
      {
        onSuccess: () => toast({ tone: 'success', title: `${name} surplus moved to the Organization fund.` }),
        onError: (e) => toast({ tone: 'error', title: 'Could not transfer the surplus.', description: errorMessage(e) }),
      },
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(200px,1fr))]">
        <Stat label="Money in" value={formatPeso(overview.moneyIn)} sub="Posted entries" />
        <Stat label="Money out" value={formatPeso(overview.moneyOut)} sub="Posted entries" />
        <Stat label="Net" value={formatPesoSigned(overview.net)} sub="In minus out" />
        <Stat
          label="Waiting for approval"
          value={overview.pendingCount}
          sub={overview.pendingCount === 0 ? 'Nothing pending' : formatPeso(overview.pendingAmount)}
        />
      </div>

      <Card>
        <CardHeader title="Money in and out" description="By month, from posted entries only." as="h3" />
        <CardBody>
          {overview.cashflow.length === 0 ? (
            <EmptyState
              icon={<WalletIcon size={22} />}
              title="No posted entries yet"
              description="Once money is recorded, the months show up here."
            />
          ) : (
            <CashflowChart points={overview.cashflow} />
          )}
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="Funds" description="The Organization fund, and one per event." as="h3" />
        <CardBody>
          <Table minWidth={560}>
            <THead>
              <Tr>
                <Th>Fund</Th>
                <Th>Status</Th>
                <Th>Balance</Th>
                <Th>
                  <span className="sr-only">Actions</span>
                </Th>
              </Tr>
            </THead>
            <TBody>
              {overview.funds.map((fund) => (
                <Tr key={fund.id}>
                  <Td>{fund.name}</Td>
                  <Td>
                    {fund.isClosed ? 'Closed' : fund.isSwept ? 'Surplus transferred' : 'Open'}
                  </Td>
                  <Td numeric>{formatPesoSigned(fund.balance)}</Td>
                  <Td>
                    {fund.canSweep ? (
                      <Button
                        variant="secondary"
                        size="sm"
                        loading={sweep.isPending}
                        onClick={() => onSweep(fund.id, fund.name)}
                      >
                        Transfer surplus
                      </Button>
                    ) : null}
                  </Td>
                </Tr>
              ))}
            </TBody>
          </Table>
        </CardBody>
      </Card>
    </div>
  )
}
