import { useState } from 'react'
import { useParams } from 'react-router'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Card, CardBody, CardHeader } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Menu } from '@/components/ui/Menu'
import { ProgressBar } from '@/components/ui/ProgressBar'
import { Skeleton } from '@/components/ui/Spinner'
import { Stat } from '@/components/ui/Stat'
import { Table, TBody, Td, Th, THead, Tr } from '@/components/ui/Table'
import { BuildingIcon, ChartIcon } from '@/components/ui/icons'
import { formatDate, formatPeso, formatPesoSigned } from '@/lib/format'
import { useBudgetQuery, useBudgetVersionQuery } from './api'
import type { BudgetLine } from './schemas'
import { BudgetLinesForm } from './components/BudgetLinesForm'

/**
 * Frame 25: an event's budget against its actuals. The actuals are never stored — they are the posted entries of the
 * event's fund, matched to a line by category — so this screen is always telling the truth about the ledger.
 */
export function BudgetPage() {
  const { eventId = '' } = useParams()
  const [editing, setEditing] = useState(false)
  const [versionId, setVersionId] = useState<string | null>(null)

  const current = useBudgetQuery(eventId)
  const historical = useBudgetVersionQuery(eventId, versionId)
  const query = versionId ? historical : current
  const budget = query.data

  if (query.isPending) {
    return (
      <div className="flex flex-col gap-4" role="status" aria-label="Loading the budget…">
        <Skeleton className="h-24" />
        <Skeleton className="h-64" />
      </div>
    )
  }

  if (query.isError) {
    if (isApiError(query.error, 404)) {
      return (
        <Card>
          <EmptyState icon={<BuildingIcon size={22} />} title="Event not found" description="It may have been removed." />
        </Card>
      )
    }
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
        Could not load the budget. {errorMessage(query.error)}
      </Alert>
    )
  }

  if (!budget) return null
  const isHistorical = versionId !== null
  const totals = budget.totals
  const income = budget.lines.filter((l) => l.kind === 'Income')
  const expense = budget.lines.filter((l) => l.kind === 'Expense')

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">{budget.eventName}</h2>
          <p className="text-sm text-gray-600 dark:text-gray-400">
            {budget.version
              ? `Version ${budget.version.versionNumber} · ${budget.version.createdBy ?? 'the CFO'} · ${formatDate(budget.version.createdAt)}`
              : 'No budget saved yet.'}
          </p>
        </div>
        <div className="flex items-center gap-2">
          {budget.history.length > 0 ? (
            <Menu
              aria-label="Budget versions"
              variant="secondary"
              items={[
                { label: 'Current version', onSelect: () => setVersionId(null), selected: !isHistorical },
                ...budget.history.map((v) => ({
                  label: `Version ${v.versionNumber} · ${formatDate(v.createdAt)}`,
                  onSelect: () => setVersionId(v.id),
                  selected: versionId === v.id,
                })),
              ]}
            >
              Version history
            </Menu>
          ) : null}
          {budget.canEdit && !isHistorical ? (
            <Button onClick={() => setEditing(true)}>{budget.version ? 'Edit budget' : 'Start the budget'}</Button>
          ) : null}
        </div>
      </div>

      {isHistorical ? (
        <Alert tone="blue" live="status">
          You are looking at version {budget.version?.versionNumber}: “{budget.version?.reason}”.
        </Alert>
      ) : null}
      {!budget.canEdit ? (
        <Alert tone="blue">Budgets are edited by the CFO. You are seeing the current version.</Alert>
      ) : null}

      <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(200px,1fr))]">
        <Stat label="Income budgeted" value={formatPeso(totals.incomeBudgeted)} sub={`${formatPeso(totals.incomeActual)} actual`} />
        <Stat label="Expenses budgeted" value={formatPeso(totals.expenseBudgeted)} sub={`${formatPeso(totals.expenseActual)} actual`} />
        <Stat label="Net budgeted" value={formatPesoSigned(totals.netBudgeted)} sub={`${formatPesoSigned(totals.netActual)} actual`} />
        <Stat label="Fund balance" value={formatPesoSigned(budget.fundBalance)} sub="Posted entries" />
      </div>

      {budget.lines.length === 0 ? (
        <Card>
          <EmptyState
            icon={<ChartIcon size={22} />}
            title="No budget yet"
            description={
              budget.canEdit
                ? 'Add the income and expense lines you expect. Actuals fill in from the ledger as money moves.'
                : 'The CFO has not saved a budget for this event yet.'
            }
            action={budget.canEdit ? <Button onClick={() => setEditing(true)}>Start the budget</Button> : undefined}
          />
        </Card>
      ) : (
        <>
          <BudgetSection title="Income" lines={income} />
          <BudgetSection title="Expenses" lines={expense} />
        </>
      )}

      <Card>
        <CardHeader
          title="Suppliers for this event"
          description="Hired through the budget: contract, paid so far and what is next."
          as="h3"
        />
        <CardBody>
          <EmptyState
            icon={<BuildingIcon size={22} />}
            title="Suppliers arrive with the directory"
            description="The supplier list, ratings and their link to these budget lines ship in the directory and suppliers slice."
          />
        </CardBody>
      </Card>

      {budget.canEdit ? (
        <BudgetLinesForm budget={budget} open={editing} onClose={() => setEditing(false)} />
      ) : null}
    </div>
  )
}

function BudgetSection({ title, lines }: { title: string; lines: BudgetLine[] }) {
  if (lines.length === 0) return null
  return (
    <Card>
      <CardHeader title={title} as="h3" />
      <CardBody className="p-0">
        <Table minWidth={720}>
          <THead>
            <Tr>
              <Th>Line</Th>
              <Th>Category</Th>
              <Th>Budgeted</Th>
              <Th>Actual</Th>
              <Th>Variance</Th>
              <Th>Progress</Th>
            </Tr>
          </THead>
          <TBody>
            {lines.map((line) => {
              const pct = line.amountBudgeted === 0 ? 0 : (line.actual / line.amountBudgeted) * 100
              // Over budget on an expense line, or short on income: red is backed by the words in the variance cell.
              const over = line.variance < 0
              return (
                <Tr key={line.id}>
                  <Td>{line.label}</Td>
                  <Td>{line.categoryName}</Td>
                  <Td numeric>{formatPeso(line.amountBudgeted)}</Td>
                  <Td numeric>{formatPeso(line.actual)}</Td>
                  <Td numeric>
                    <span className={over ? 'text-red-700 dark:text-red-400' : undefined}>
                      {formatPesoSigned(line.variance)} {over ? (line.kind === 'Income' ? 'short' : 'over') : 'to spare'}
                    </span>
                  </Td>
                  <Td>
                    <ProgressBar
                      value={pct}
                      tone={over ? 'brand' : 'green'}
                      label={`${line.label}: ${formatPeso(line.actual)} of ${formatPeso(line.amountBudgeted)}`}
                    />
                  </Td>
                </Tr>
              )
            })}
          </TBody>
        </Table>
      </CardBody>
    </Card>
  )
}
