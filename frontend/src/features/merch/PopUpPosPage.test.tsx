import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { buyerHit } from '@/test/fixtures/merch'
import { leftoverVariant, popUp, popUpAllocation } from '@/test/fixtures/popups'
import { boardMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { PopUpPosPage } from './PopUpPosPage'

function mockPos(p: ReturnType<typeof popUp> = popUp()) {
  server.use(
    http.get('/api/pop-ups/pu1', () => HttpResponse.json(p)),
    http.get('/api/merch/leftover-inventory', () => HttpResponse.json([leftoverVariant()])),
    http.get('/api/pop-ups/pu1/pos/buyer-search', ({ request }) => {
      const q = new URL(request.url).searchParams.get('q') ?? ''
      return HttpResponse.json(q.length >= 3 ? [buyerHit()] : [])
    }),
  )
}

function renderPage() {
  return renderRoutes([{ path: '/pop-ups/:id/pos', element: <PopUpPosPage /> }], '/pop-ups/pu1/pos')
}

describe('Pop-up POS (frame 21b)', () => {
  it('shows the product grid scoped to this pop-up\'s own allocated stock', async () => {
    signInAs(boardMe)
    mockPos()
    renderPage()

    const tile = await screen.findByRole('button', { name: /HTN Con Tee S/ })
    expect(tile).toHaveAccessibleName(expect.stringContaining('7 left'))
  })

  it('disables a sold-out allocation and has no Scan QR option', async () => {
    signInAs(boardMe)
    mockPos(popUp({ allocations: [popUpAllocation({ qtyBrought: 5, qtySold: 5 })] }))
    renderPage()

    const tile = await screen.findByRole('button', { name: /out of stock/ })
    expect(tile).toBeDisabled()
    expect(screen.queryByRole('button', { name: /Scan QR/ })).not.toBeInTheDocument()
  })

  it('searches buyers (People only) and completes a sale', async () => {
    signInAs(boardMe)
    mockPos()
    let saleBody: Record<string, unknown> | null = null
    server.use(
      http.post('/api/pop-ups/pu1/pos/sale', async ({ request }) => {
        saleBody = (await request.json()) as Record<string, unknown>
        return HttpResponse.json({
          id: 's1',
          eventId: null,
          popUpId: 'pu1',
          buyerPersonId: 'p2',
          buyerAttendeeId: null,
          buyerNameSnapshot: 'Mika Soriano',
          buyerLevelSnapshot: 'Goon',
          subtotal: 450,
          discountPct: 0,
          discountAmount: 0,
          total: 450,
          method: 'Cash',
          cashierId: 'u1',
          station: 'Booth 1',
          soldAt: '2026-11-10T03:00:00Z',
          lines: [{ id: 'l1', itemId: 'i1', itemName: 'HTN Con Tee', variantId: 'v1', variantLabel: 'S', qty: 1, unitPriceSnapshot: 450, lineTotal: 450 }],
          membershipFeeIncluded: false,
          membershipFeeAmount: null,
        })
      }),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: /HTN Con Tee S/ }))
    await user.type(screen.getByLabelText('Search buyer by name'), 'Mika')
    await user.click(await screen.findByRole('button', { name: /Mika Soriano/ }))
    await user.click(screen.getByRole('button', { name: /Complete sale/ }))

    expect(await screen.findByText('Sale recorded')).toBeInTheDocument()
    expect(saleBody).toMatchObject({ buyerPersonId: 'p2', buyerAttendeeId: null, lines: [{ variantId: 'v1', qty: 1 }], method: 'Cash' })
  })
})
