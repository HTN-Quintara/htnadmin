import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { eventMerchSettings, merchDashboard, merchItem } from '@/test/fixtures/merch'
import { boardMe, committeeLeadMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { MerchPage } from './MerchPage'

function mockMerch(dashboard: ReturnType<typeof merchDashboard> = merchDashboard()) {
  server.use(
    http.get('/api/events/e1/merch', () => HttpResponse.json(dashboard)),
    http.get('/api/events/e1/merch/discounts', () => HttpResponse.json(eventMerchSettings())),
  )
}

function renderPage() {
  return renderRoutes([{ path: '/events/:eventId/merch', element: <MerchPage /> }], '/events/e1/merch')
}

describe('Merch (frame 20)', () => {
  it('renders KPIs, inventory and sales for a board account', async () => {
    signInAs(boardMe)
    mockMerch()
    renderPage()

    const salesCard = (await screen.findByText('Merch sales')).closest('div')
    expect(salesCard).toBeInstanceOf(HTMLElement)
    expect(within(salesCard as HTMLElement).getByText('₱360')).toBeInTheDocument()
    expect(within(salesCard as HTMLElement).getByText('1 orders')).toBeInTheDocument()

    const inventory = screen.getByText('HTN Con Tee').closest('table')
    expect(inventory).toBeInstanceOf(HTMLElement)
    expect(within(inventory as HTMLElement).getByText('Tote bag')).toBeInTheDocument()

    expect(screen.getByText('Sales · who bought what')).toBeInTheDocument()
    expect(screen.getByText('Mika Soriano')).toBeInTheDocument()
    expect(screen.getByText('−₱90 (20%)')).toBeInTheDocument()

    expect(screen.getByRole('link', { name: 'Export CSV' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '+ Item' })).toBeInTheDocument()
  })

  it('writes a discount change through PUT /merch/discounts', async () => {
    signInAs(boardMe)
    mockMerch()
    let saved: unknown = null
    server.use(
      http.put('/api/events/e1/merch/discounts', async ({ request }) => {
        saved = await request.json()
        return HttpResponse.json(eventMerchSettings({ goonPct: 25 }))
      }),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: 'Increase Goon discount' }))

    expect(saved).toMatchObject({ goonPct: 25, apprenticePct: 15, memberPct: 10, boardPct: 0 })
  })

  it('creates an item through the + Item dialog', async () => {
    signInAs(boardMe)
    mockMerch(merchDashboard({ items: [] }))
    let created: unknown = null
    server.use(
      http.post('/api/events/e1/merch/items', async ({ request }) => {
        created = await request.json()
        return HttpResponse.json(merchItem({ id: 'i9', name: 'Sticker pack', price: 50, variants: [] }))
      }),
      http.post('/api/events/e1/merch/items/i9/variants', () => HttpResponse.json({ id: 'v9', itemId: 'i9', label: 'One size', stockOnHand: 40, isActive: true, lowStock: false })),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: '+ Item' }))
    const dialog = await screen.findByRole('dialog', { name: '+ Item' })
    await user.type(within(dialog).getByLabelText('Name'), 'Sticker pack')
    await user.clear(within(dialog).getByLabelText('Price'))
    await user.type(within(dialog).getByLabelText('Price'), '50')
    await user.type(within(dialog).getByLabelText('Label'), 'One size')
    await user.clear(within(dialog).getByLabelText('Starting stock'))
    await user.type(within(dialog).getByLabelText('Starting stock'), '40')
    await user.click(within(dialog).getByRole('button', { name: 'Add item' }))

    expect(await screen.findByText('Sticker pack added to the catalog.')).toBeInTheDocument()
    expect(created).toMatchObject({ name: 'Sticker pack', price: 50 })
  })

  it('hides manage-only actions for a committee lead who can only sell', async () => {
    signInAs(committeeLeadMe)
    mockMerch(merchDashboard({ canManage: false, kpi: { ...merchDashboard().kpi, totalSales: null, discountsGiven: null } }))
    server.use(http.get('/api/events/e1/merch/discounts', () => HttpResponse.json({ status: 403, title: 'Forbidden' }, { status: 403 })))
    renderPage()

    await screen.findByText('Merch sales')
    expect(screen.queryByRole('button', { name: '+ Item' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Export CSV' })).not.toBeInTheDocument()
    expect(screen.getByText('Visible to the board only.')).toBeInTheDocument()
  })
})
