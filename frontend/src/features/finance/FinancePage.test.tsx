import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { routes } from '@/app/router'
import {
  balancing,
  entry,
  financeHandlers,
  ledgerPage,
  period,
} from '@/test/fixtures/finance'
import { boardMe, cfoMe, financeAssistantMe, goonMe, problem, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'

function useFinanceApi() {
  server.use(...financeHandlers(http, (body) => HttpResponse.json(body)))
}

function renderFinance(path = '/finance') {
  const user = userEvent.setup()
  return { user, ...renderRoutes(routes, path) }
}

describe('Finance (frame 24)', () => {
  it('bounces anyone without Finance.View before any request is made', async () => {
    signInAs(goonMe)
    renderFinance()

    expect(await screen.findByText(/do not have access to this page/i)).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Finance' })).not.toBeInTheDocument()
  })

  it('tells a board member who is not the CFO what they can and cannot do', async () => {
    signInAs(boardMe)
    useFinanceApi()
    renderFinance()

    expect(
      await screen.findByText(/Only the CFO posts entries, reconciles accounts and closes months/i),
    ).toBeInTheDocument()
    // Three screens, and the four sub-views.
    expect(await screen.findByRole('link', { name: /Expense requests/i })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Cashflow' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Audit' })).toBeInTheDocument()
  })

  it('shows the finance assistant only the ledger, and tells them their entries wait', async () => {
    signInAs(financeAssistantMe)
    useFinanceApi()
    renderFinance()

    expect(await screen.findByText(/Each one waits for the CFO to approve it/i)).toBeInTheDocument()
    // No cashflow, balancing or audit: they would 403.
    expect(screen.queryByRole('radio', { name: 'Cashflow' })).not.toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: 'Balancing' })).not.toBeInTheDocument()
    // But they can add an entry.
    expect(await screen.findByRole('button', { name: 'New entry' })).toBeInTheDocument()
  })

  it('shows the cashflow totals and the fund balances', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    renderFinance()

    expect(await screen.findByText('₱180,000')).toBeInTheDocument() // money in
    expect(screen.getByText('₱55,000')).toBeInTheDocument() // money out
    // The net carries its sign, in the stat tile and again on the Organization fund row.
    expect(screen.getAllByText('+₱125,000')).toHaveLength(2)
    // The chart carries its numbers as text too, not only as bars.
    expect(screen.getByRole('img', { name: /Money in and out by month/i })).toBeInTheDocument()
    expect(screen.getByRole('table', { name: /Money in and out by month/i })).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Transfer surplus' })).toBeInTheDocument()
  })

  it('hides New entry and the row actions from a board member who is not the CFO', async () => {
    signInAs(boardMe)
    useFinanceApi()
    server.use(
      http.get('/api/finance/ledger', () =>
        HttpResponse.json(ledgerPage([entry({ status: 'Pending', canApprove: false, canVoid: false })])),
      ),
    )
    const { user } = renderFinance()

    await user.click(await screen.findByRole('radio', { name: 'Ledger' }))

    expect(await screen.findByRole('table')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'New entry' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Actions for/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Export CSV' })).not.toBeInTheDocument()
  })

  it('renders the ledger with right-aligned amounts and the status in words', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    server.use(
      http.get('/api/finance/ledger', () =>
        HttpResponse.json(
          ledgerPage([
            entry(),
            entry({ id: 'entry-2', status: 'Pending', description: 'Snacks', direction: 'Out', amount: 900, signedAmount: -900, canApprove: true }),
          ]),
        ),
      ),
    )
    const { user } = renderFinance()

    await user.click(await screen.findByRole('radio', { name: 'Ledger' }))
    const table = await screen.findByRole('table')

    expect(within(table).getByText('Ticket payment · HTN-ABC123')).toBeInTheDocument()
    expect(within(table).getByText('Posted')).toBeInTheDocument()
    expect(within(table).getByText('Pending')).toBeInTheDocument()
    // Money is right-aligned and tabular so columns line up when reconciling.
    const amount = within(table).getByText('₱2,500')
    expect(amount).toHaveClass('tabular-nums')
    // In and Out are words, never colour alone.
    expect(within(table).getByText('In')).toBeInTheDocument()
    expect(within(table).getByText('Out')).toBeInTheDocument()
  })

  it('offers both empty-state copies: nothing yet, and nothing matching', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    server.use(http.get('/api/finance/ledger', () => HttpResponse.json(ledgerPage([]))))
    const { user } = renderFinance()

    await user.click(await screen.findByRole('radio', { name: 'Ledger' }))
    expect(await screen.findByText('No entries yet')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Waiting' }))
    expect(await screen.findByText('No entries match these filters')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Clear filters' })).toBeInTheDocument()
  })

  it('shows a retry when the ledger cannot be loaded', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    server.use(http.get('/api/finance/ledger', () => problem(500, 'Server error')))
    const { user } = renderFinance()

    await user.click(await screen.findByRole('radio', { name: 'Ledger' }))
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(/Could not load the ledger/i)
    expect(within(alert).getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })

  it('explains exactly why a month cannot be closed yet', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    server.use(
      http.get('/api/finance/accounts', () =>
        HttpResponse.json(
          balancing({
            period: period({
              canClose: false,
              blockers: [
                { code: 'month_has_pending', message: '1 entry is still waiting for your approval.' },
                { code: 'month_unbalanced', message: 'Not balanced yet: GCash main.' },
              ],
            }),
          }),
        ),
      ),
    )
    const { user } = renderFinance()

    await user.click(await screen.findByRole('radio', { name: 'Balancing' }))

    // The blockers are on the page, not hidden behind a disabled button.
    const notice = await screen.findByRole('status')
    expect(notice).toHaveTextContent(/1 entry is still waiting for your approval/i)
    expect(notice).toHaveTextContent(/Not balanced yet: GCash main/i)
    expect(screen.queryByRole('button', { name: 'Close month' })).not.toBeInTheDocument()
  })

  it('surfaces the server message when closing the month is refused', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    server.use(
      http.post('/api/finance/periods/:year/:month/close', () =>
        problem(409, 'This month cannot be closed yet.', 'month_unbalanced'),
      ),
    )
    const { user } = renderFinance()

    await user.click(await screen.findByRole('radio', { name: 'Balancing' }))
    await user.click(await screen.findByRole('button', { name: 'Close month' }))
    await user.click(await screen.findByRole('button', { name: 'Close the month' }))

    expect(
      await screen.findByText(/Some accounts are not balanced yet\. Reconcile them before closing the month\./i),
    ).toBeInTheDocument()
  })

  it('shows each account with its balanced state in words', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    const { user } = renderFinance()

    await user.click(await screen.findByRole('radio', { name: 'Balancing' }))
    const table = await screen.findByRole('table')

    expect(within(table).getByText('GCash main')).toBeInTheDocument()
    expect(within(table).getByText('Needs check')).toBeInTheDocument() // never reconciled
    expect(within(table).getByText('Balanced')).toBeInTheDocument() // cash on hand, difference 0
    expect(within(table).getAllByRole('button', { name: /Reconcile/ }).length).toBeGreaterThan(0)
  })

  it('requires a reason before voiding an entry', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    server.use(http.get('/api/finance/ledger', () => HttpResponse.json(ledgerPage([entry({ canVoid: true })]))))
    const { user } = renderFinance()

    await user.click(await screen.findByRole('radio', { name: 'Ledger' }))
    await user.click(await screen.findByRole('button', { name: /Actions for/i }))
    await user.click(await screen.findByRole('menuitem', { name: 'Void…' }))

    const dialog = await screen.findByRole('alertdialog')
    await user.click(within(dialog).getByRole('button', { name: 'Void entry' }))

    expect(await within(dialog).findByText('A reason is required.')).toBeInTheDocument()
  })

  it('lists the audit trail read-only', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    const { user } = renderFinance()

    await user.click(await screen.findByRole('radio', { name: 'Audit' }))
    const table = await screen.findByRole('table')

    expect(within(table).getByText('Month closed')).toBeInTheDocument()
    expect(within(table).getByText('Noel Cruz')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('link', { name: 'Export CSV' })).toBeInTheDocument())
  })
})
