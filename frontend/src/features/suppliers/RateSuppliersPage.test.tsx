import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { toRateHire } from '@/test/fixtures/suppliers'
import { committeeLeadMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { RateSuppliersPage } from './RateSuppliersPage'

function mockToRate(hires: ReturnType<typeof toRateHire>[]) {
  let current = hires
  server.use(http.get('/api/events/e1/suppliers/to-rate', () => HttpResponse.json(current)))
  return (next: typeof hires) => { current = next }
}

function renderPage() {
  return renderRoutes([{ path: '/tasks/rate-suppliers/:eventId', element: <RateSuppliersPage /> }], '/tasks/rate-suppliers/e1')
}

describe('Rate suppliers (frame 18c)', () => {
  it('renders hires to rate from fixtures', async () => {
    signInAs(committeeLeadMe)
    mockToRate([toRateHire(), toRateHire({ id: 'es3', supplierName: 'Lami Catering Co.' })])
    renderPage()

    expect(await screen.findByText('Grand Hall Events Place')).toBeInTheDocument()
    expect(screen.getByText('Lami Catering Co.')).toBeInTheDocument()
    expect(screen.getByText('0 of 2 rated')).toBeInTheDocument()
  })

  it('disables submit until stars and hire-again are both set, and multi-selects tags', async () => {
    signInAs(committeeLeadMe)
    mockToRate([toRateHire()])
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Grand Hall Events Place')
    const card = screen.getByText('Grand Hall Events Place').closest('section') as HTMLElement
    const submit = within(card).getByRole('button', { name: 'Submit rating' })
    expect(submit).toBeDisabled()

    await user.click(within(card).getByRole('radio', { name: '4 stars' }))
    expect(submit).toBeDisabled()

    await user.click(within(card).getByRole('radio', { name: 'Yes' }))
    expect(submit).toBeEnabled()

    await user.click(within(card).getByLabelText('On time'))
    await user.click(within(card).getByLabelText('Fair price'))
    expect(within(card).getByLabelText('On time')).toBeChecked()
    expect(within(card).getByLabelText('Fair price')).toBeChecked()
    await user.click(within(card).getByLabelText('On time'))
    expect(within(card).getByLabelText('On time')).not.toBeChecked()
  })

  it('removes a submitted card from the list and updates the progress bar', async () => {
    signInAs(committeeLeadMe)
    const setToRate = mockToRate([toRateHire(), toRateHire({ id: 'es3', supplierName: 'Lami Catering Co.' })])
    server.use(
      http.post('/api/events/e1/suppliers/es2/ratings', () => {
        setToRate([toRateHire({ id: 'es3', supplierName: 'Lami Catering Co.' })])
        return HttpResponse.json({ id: 'r1', eventSupplierId: 'es2', raterId: 'u6', stars: 5, tags: [], hireAgain: 'Yes', note: null, createdAt: '2026-10-28T00:00:00Z' })
      }),
    )
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Grand Hall Events Place')
    expect(screen.getByText('0 of 2 rated')).toBeInTheDocument()

    const card = screen.getByText('Grand Hall Events Place').closest('section') as HTMLElement
    await user.click(within(card).getByRole('radio', { name: '5 stars' }))
    await user.click(within(card).getByRole('radio', { name: 'Yes' }))
    await user.click(within(card).getByRole('button', { name: 'Submit rating' }))

    expect(await screen.findByText('1 of 2 rated')).toBeInTheDocument()
    expect(screen.queryByText('Grand Hall Events Place')).not.toBeInTheDocument()
    expect(screen.getByText('Lami Catering Co.')).toBeInTheDocument()
  })
})
