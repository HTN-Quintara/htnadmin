import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { supplier, suppliersList } from '@/test/fixtures/suppliers'
import { boardMe, committeeLeadMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { SuppliersPage } from './SuppliersPage'

function mockList(asBoard: boolean) {
  server.use(
    http.get('/api/suppliers', ({ request }) => {
      const url = new URL(request.url)
      const category = url.searchParams.get('category')
      let list = suppliersList(asBoard)
      if (category) list = list.filter((s) => s.category === category)
      return HttpResponse.json(list)
    }),
  )
}

function mockDetail(asBoard: boolean) {
  server.use(
    http.get('/api/suppliers/s1', () =>
      HttpResponse.json({ supplier: supplier(asBoard), documents: [], hires: [] }),
    ),
  )
}

function renderPage() {
  return renderRoutes([{ path: '/directory/suppliers', element: <SuppliersPage /> }], '/directory/suppliers')
}

describe('Suppliers (frame 18b)', () => {
  it('lists suppliers with category filter chips', async () => {
    signInAs(boardMe)
    mockList(true)
    const user = userEvent.setup()
    renderPage()

    expect(await screen.findByText('Grand Hall Events Place')).toBeInTheDocument()
    expect(screen.getByText('Lami Catering Co.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Catering' }))
    expect(await screen.findByText('Lami Catering Co.')).toBeInTheDocument()
    expect(screen.queryByText('Grand Hall Events Place')).not.toBeInTheDocument()
  })

  it('shows TIN and bank details to a board caller', async () => {
    signInAs(boardMe)
    mockList(true)
    mockDetail(true)
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: 'Grand Hall Events Place' }))
    expect(await screen.findByText('123-456-789-000')).toBeInTheDocument()
    expect(screen.getByText('BDO 0012-3456-7890')).toBeInTheDocument()
  })

  it("omits TIN and bank details for a committee lead, since the API never sends them", async () => {
    signInAs(committeeLeadMe)
    mockList(false)
    mockDetail(false)
    const user = userEvent.setup()
    renderPage()

    expect(await screen.findByText('Grand Hall Events Place')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Supplier' })).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Grand Hall Events Place' }))
    await screen.findByRole('heading', { name: 'Grand Hall Events Place' })
    expect(screen.queryByText('123-456-789-000')).not.toBeInTheDocument()
    expect(screen.queryByText('TIN')).not.toBeInTheDocument()
  })

  it('toggles the Board/CFO vs Committee lead preview without an extra API call', async () => {
    signInAs(boardMe)
    mockList(true)
    mockDetail(true)
    let detailCalls = 0
    server.use(
      http.get('/api/suppliers/s1', () => {
        detailCalls += 1
        return HttpResponse.json({ supplier: supplier(true), documents: [], hires: [] })
      }),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: 'Grand Hall Events Place' }))
    expect(await screen.findByText('123-456-789-000')).toBeInTheDocument()
    const callsAfterOpen = detailCalls

    await user.click(screen.getByRole('radio', { name: 'Committee lead' }))
    expect(screen.queryByText('123-456-789-000')).not.toBeInTheDocument()
    // The toggle is local display state: hiding the already-fetched boardOnly field does not refetch.
    expect(detailCalls).toBe(callsAfterOpen)

    await user.click(screen.getByRole('radio', { name: 'Preview as Board/CFO' }))
    expect(await screen.findByText('123-456-789-000')).toBeInTheDocument()
    expect(detailCalls).toBe(callsAfterOpen)
  })
})
