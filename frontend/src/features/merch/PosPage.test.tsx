import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { buyerHit, eventMerchSettings, merchDashboard, merchSale } from '@/test/fixtures/merch'
import { detail as eventDetail } from '@/test/fixtures/events'
import { boardMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { PosPage } from './PosPage'

function mockEvent() {
  server.use(http.get('/api/events/e1', () => HttpResponse.json(eventDetail())))
}

function mockMerch() {
  server.use(
    http.get('/api/events/e1/merch', () => HttpResponse.json(merchDashboard())),
    http.get('/api/events/e1/merch/discounts', () => HttpResponse.json(eventMerchSettings())),
  )
}

function mockBuyerSearch() {
  server.use(http.get('/api/events/e1/pos/buyer-search', () => HttpResponse.json([buyerHit()])))
}

function renderPage() {
  return renderRoutes([{ path: '/events/:eventId/pos', element: <PosPage /> }], '/events/e1/pos')
}

describe('Merch POS (frame 21)', () => {
  it('adds items to the cart from the product grid', async () => {
    signInAs(boardMe)
    mockEvent()
    mockMerch()
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Tap a size on the left to add an item')
    await user.click(await screen.findByRole('button', { name: /^Add HTN Con Tee S to the cart/ }))

    expect(await screen.findByText('HTN Con Tee (S)')).toBeInTheDocument()
    expect(screen.getByText('Subtotal')).toBeInTheDocument()
  })

  it('searches for a buyer by name and picks one', async () => {
    signInAs(boardMe)
    mockEvent()
    mockMerch()
    mockBuyerSearch()
    const user = userEvent.setup()
    renderPage()

    await user.type(await screen.findByLabelText('Search buyer by name'), 'Mika')
    const hit = await screen.findByRole('button', { name: /Mika Soriano/ })
    await user.click(hit)

    expect(await screen.findByText('Mika Soriano')).toBeInTheDocument()
    expect(screen.getByText('Goon · 20% merch discount')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Change' })).toBeInTheDocument()
  })

  it('clears the buyer with Change', async () => {
    signInAs(boardMe)
    mockEvent()
    mockMerch()
    mockBuyerSearch()
    const user = userEvent.setup()
    renderPage()

    await user.type(await screen.findByLabelText('Search buyer by name'), 'Mika')
    await user.click(await screen.findByRole('button', { name: /Mika Soriano/ }))
    await screen.findByRole('button', { name: 'Change' })
    await user.click(screen.getByRole('button', { name: 'Change' }))

    expect(await screen.findByLabelText('Search buyer by name')).toBeInTheDocument()
  })

  it('computes subtotal/discount/total from qty steppers, gates the submit button and completes a sale', async () => {
    signInAs(boardMe)
    mockEvent()
    mockMerch()
    mockBuyerSearch()
    server.use(
      http.post('/api/events/e1/pos/sale', () => HttpResponse.json(merchSale({ subtotal: 900, discountAmount: 180, total: 720 }))),
    )
    const user = userEvent.setup()
    renderPage()

    const submit = await screen.findByRole('button', { name: /Complete sale/ })
    expect(submit).toBeDisabled()

    await user.click(screen.getByRole('button', { name: /^Add HTN Con Tee S to the cart/ }))
    expect(screen.getByRole('button', { name: /Complete sale/ })).toBeDisabled()
    expect(screen.getByText('Identify the buyer first so the sale is tied to a person.')).toBeInTheDocument()

    await user.type(screen.getByLabelText('Search buyer by name'), 'Mika')
    await user.click(await screen.findByRole('button', { name: /Mika Soriano/ }))

    // Bump quantity to 2 (₱450 × 2 = ₱900, 20% discount = ₱180, total ₱720).
    await user.click(screen.getByRole('button', { name: /Increase HTN Con Tee S/ }))

    const subtotalRow = screen.getByText('Subtotal').closest('div')
    expect(subtotalRow).toBeInstanceOf(HTMLElement)
    expect(within(subtotalRow as HTMLElement).getByText('₱900')).toBeInTheDocument()
    expect(screen.getByText('−₱180')).toBeInTheDocument()

    const enabledSubmit = screen.getByRole('button', { name: 'Complete sale · ₱720' })
    expect(enabledSubmit).toBeEnabled()
    await user.click(enabledSubmit)

    expect(await screen.findByText('Sale recorded')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'New sale' })).toBeInTheDocument()
  })

  it('shows the radio payment method group with cash selected by default', async () => {
    signInAs(boardMe)
    mockEvent()
    mockMerch()
    renderPage()

    await screen.findByText('Payment method')
    const group = screen.getByRole('radiogroup', { name: 'Payment method' })
    expect(within(group).getByRole('radio', { name: 'Cash', checked: true })).toBeInTheDocument()
    expect(within(group).getByRole('radio', { name: 'GCash' })).toBeInTheDocument()
  })

  it('surfaces an insufficient-stock error from the server', async () => {
    signInAs(boardMe)
    mockEvent()
    mockMerch()
    mockBuyerSearch()
    server.use(
      http.post('/api/events/e1/pos/sale', () =>
        HttpResponse.json({ status: 409, title: 'That cannot be done right now.', code: 'insufficient_stock' }, { status: 409 }),
      ),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: /^Add HTN Con Tee S to the cart/ }))
    await user.type(screen.getByLabelText('Search buyer by name'), 'Mika')
    await user.click(await screen.findByRole('button', { name: /Mika Soriano/ }))
    await user.click(screen.getByRole('button', { name: /Complete sale/ }))

    expect(await screen.findByText('Not enough stock left. Reload and try again.')).toBeInTheDocument()
  })
})
