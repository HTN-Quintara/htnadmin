import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { routes } from '@/app/router'
import { financeHandlers, request, requestPage } from '@/test/fixtures/finance'
import { boardMe, cfoMe, problem, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'

function useFinanceApi() {
  server.use(...financeHandlers(http, (body) => HttpResponse.json(body)))
}

function renderRequests(path = '/finance/requests') {
  const user = userEvent.setup()
  return { user, ...renderRoutes(routes, path) }
}

describe('Expense requests (frame 26)', () => {
  it('lets a board member file a request', async () => {
    signInAs(boardMe)
    useFinanceApi()
    let sent: unknown = null
    server.use(
      http.post('/api/finance/requests', async ({ request: req }) => {
        sent = await req.json()
        return HttpResponse.json(request())
      }),
    )
    const { user } = renderRequests()

    await user.click(await screen.findByRole('button', { name: 'New request' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText(/What is the money for/i), 'Year-end party')
    await user.type(within(dialog).getByLabelText(/Amount/i), '40000')
    await user.selectOptions(within(dialog).getByLabelText('Category'), 'cat-writeoff')
    await user.click(within(dialog).getByRole('button', { name: 'File request' }))

    expect(await screen.findByText(/Request filed/i)).toBeInTheDocument()
    expect(sent).toMatchObject({ title: 'Year-end party', amount: 40000, categoryId: 'cat-writeoff' })
  })

  it('shows the amount and status for each request', async () => {
    signInAs(boardMe)
    useFinanceApi()
    renderRequests()

    const table = await screen.findByRole('table')
    expect(within(table).getByText('Year-end party')).toBeInTheDocument()
    expect(within(table).getByText('₱40,000')).toBeInTheDocument()
    expect(within(table).getByText('Pending')).toBeInTheDocument()
  })

  it('offers Approve and Reject only when the server says they are allowed', async () => {
    signInAs(boardMe)
    useFinanceApi()
    const { user } = renderRequests()

    await user.click(await screen.findByRole('button', { name: 'Open' }))
    const drawer = await screen.findByRole('dialog')

    // A board member who is not the CFO can see it and do nothing to it.
    expect(within(drawer).queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(within(drawer).queryByRole('button', { name: 'Reject…' })).not.toBeInTheDocument()
    expect(within(drawer).queryByRole('button', { name: 'Mark paid' })).not.toBeInTheDocument()
  })

  it('requires a reason before rejecting', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    server.use(
      http.get('/api/finance/requests', () =>
        HttpResponse.json(requestPage([request({ canApprove: true, canReject: true })])),
      ),
    )
    const { user } = renderRequests()

    await user.click(await screen.findByRole('button', { name: 'Open' }))
    await user.click(await screen.findByRole('button', { name: 'Reject…' }))

    const dialog = await screen.findByRole('alertdialog')
    await user.click(within(dialog).getByRole('button', { name: 'Reject request' }))
    expect(await within(dialog).findByText('A reason is required.')).toBeInTheDocument()
  })

  it('needs an account and a receipt before marking a request paid', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    server.use(
      http.get('/api/finance/requests', () =>
        HttpResponse.json(requestPage([request({ status: 'Approved', canPay: true, decidedBy: 'Noel Cruz', decidedAt: '2026-10-11T02:00:00Z' })])),
      ),
    )
    const { user } = renderRequests()

    await user.click(await screen.findByRole('button', { name: 'Open' }))
    const drawer = await screen.findByRole('dialog')
    await user.click(within(drawer).getByRole('button', { name: 'Mark paid' }))

    expect(await within(drawer).findByText(/Choose the account the money came out of/i)).toBeInTheDocument()

    await user.selectOptions(within(drawer).getByLabelText(/Account the money left/i), 'acct-gcash')
    await user.click(within(drawer).getByRole('button', { name: 'Mark paid' }))
    expect(await within(drawer).findByText(/Attach the receipt for the payment/i)).toBeInTheDocument()
  })

  it('shows the decision and its reason once it has been made', async () => {
    signInAs(boardMe)
    useFinanceApi()
    server.use(
      http.get('/api/finance/requests', () =>
        HttpResponse.json(
          requestPage([
            request({
              status: 'Rejected',
              decidedBy: 'Noel Cruz',
              decidedAt: '2026-10-11T02:00:00Z',
              decisionReason: 'No budget for it this quarter',
            }),
          ]),
        ),
      ),
    )
    const { user } = renderRequests()

    await user.click(await screen.findByRole('button', { name: 'Open' }))
    const drawer = await screen.findByRole('dialog')
    expect(within(drawer).getByText('Rejected')).toBeInTheDocument()
    expect(within(drawer).getByText('No budget for it this quarter')).toBeInTheDocument()
  })

  it('offers both empty states and a retry', async () => {
    signInAs(boardMe)
    useFinanceApi()
    server.use(http.get('/api/finance/requests', () => HttpResponse.json(requestPage([]))))
    const { user } = renderRequests()

    expect(await screen.findByText('No requests yet')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Waiting' }))
    expect(await screen.findByText('Nothing matches this filter')).toBeInTheDocument()

    server.use(http.get('/api/finance/requests', () => problem(500, 'Server error')))
    await user.click(screen.getByRole('button', { name: 'Approved' }))
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(/Could not load the requests/i)
    expect(within(alert).getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})
