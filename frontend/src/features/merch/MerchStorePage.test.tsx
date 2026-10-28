import { fireEvent, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { committee, committeesPage } from '@/test/fixtures/committees'
import { summary } from '@/test/fixtures/events'
import { leftoverVariant, plannedPopUp, popUp, closedMissingPopUp } from '@/test/fixtures/popups'
import { boardMe, cfoMe, committeeLeadMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { MerchStorePage } from './MerchStorePage'

function mockBase(popUps: unknown[] = [popUp()], leftover: unknown[] = [leftoverVariant()]) {
  server.use(
    http.get('/api/pop-ups', () => HttpResponse.json(popUps)),
    http.get('/api/merch/leftover-inventory', () => HttpResponse.json(leftover)),
    http.get('/api/events', () => HttpResponse.json([summary()])),
    http.get(
      '/api/events/e1/committees',
      () =>
        HttpResponse.json(
          committeesPage({
            committees: [
              committee({ id: 'c1', name: 'Logistics', popUpsAllowed: true }),
              committee({ id: 'c2', name: 'Programs', popUpsAllowed: false }),
            ],
          }),
        ),
    ),
  )
}

function renderPage() {
  return renderRoutes([{ path: '/store', element: <MerchStorePage /> }], '/store')
}

describe('Merch store (frame 21b)', () => {
  it('shows a toggle chip per committee for a board account, and toggles it', async () => {
    signInAs(boardMe)
    mockBase()
    let saved: unknown = null
    server.use(
      http.put('/api/events/e1/committees/c2/pop-ups-allowed', async ({ request }) => {
        saved = await request.json()
        return HttpResponse.json(committee({ id: 'c2', name: 'Programs', popUpsAllowed: true }))
      }),
    )
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('Who can create pop-ups')
    const logistics = await screen.findByRole('button', { name: /Logistics/ })
    expect(logistics).toHaveAttribute('aria-pressed', 'true')
    const programs = screen.getByRole('button', { name: /Disallow Programs|Allow Programs/ })
    await user.click(programs)
    expect(saved).toEqual({ allowed: true })
  })

  it('hides the committee toggle section for a committee lead who can only sell', async () => {
    signInAs(committeeLeadMe)
    mockBase()
    renderPage()

    await screen.findByText('Merch store')
    expect(screen.queryByText('Who can create pop-ups')).not.toBeInTheDocument()
  })

  it('switches between the pop-up stores and leftover inventory tabs', async () => {
    signInAs(boardMe)
    mockBase()
    const user = userEvent.setup()
    renderPage()

    await screen.findAllByText('HTN Pop-up · SM Megamall')
    await user.click(screen.getByRole('radio', { name: /Leftover inventory/ }))
    expect(await screen.findByText('HTN Con Tee (S)')).toBeInTheDocument()
    expect(screen.queryAllByText('HTN Pop-up · SM Megamall')).toHaveLength(0)
  })

  it('shows status-dependent actions for a Planned pop-up', async () => {
    signInAs(boardMe)
    mockBase([plannedPopUp()])
    renderPage()

    await screen.findAllByText('HTN Pop-up · Ateneo bazaar')
    expect(screen.getByRole('button', { name: 'Edit stock to bring' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Open POS' })).not.toBeInTheDocument()
  })

  it('shows Open POS and Close store & count for an Open pop-up', async () => {
    signInAs(boardMe)
    mockBase([popUp()])
    renderPage()

    await screen.findAllByText('HTN Pop-up · SM Megamall')
    expect(screen.getByRole('link', { name: 'Open POS' })).toHaveAttribute('href', '/pop-ups/pu1/pos')
    expect(screen.getByRole('button', { name: 'Close store & count' })).toBeInTheDocument()
  })

  it('hides Write off from a board member who is not the CFO', async () => {
    signInAs(boardMe)
    mockBase([closedMissingPopUp()])
    renderPage()

    await screen.findAllByText('HTN Pop-up · UP Town Center')
    expect(screen.queryByRole('button', { name: 'Write off' })).not.toBeInTheDocument()
  })

  it('shows Write off for the CFO on a ClosedMissing pop-up', async () => {
    signInAs(cfoMe)
    mockBase([closedMissingPopUp()])
    renderPage()

    await screen.findAllByText('HTN Pop-up · UP Town Center')
    expect(screen.getByRole('button', { name: 'Write off' })).toBeInTheDocument()
  })

  it('creates a pop-up and then allocates leftover stock to it', async () => {
    signInAs(boardMe)
    mockBase([], [leftoverVariant()])
    let createdBody: Record<string, unknown> | null = null
    let allocatedBody: Record<string, unknown> | null = null
    server.use(
      http.get('/api/directory', () => HttpResponse.json([])),
      http.post('/api/pop-ups', async ({ request }) => {
        createdBody = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(plannedPopUp({ id: 'pu-new', name: createdBody.name }))
      }),
      http.post('/api/pop-ups/pu-new/allocations', async ({ request }) => {
        allocatedBody = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(plannedPopUp({ id: 'pu-new', name: 'HTN Pop-up · new' }))
      }),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: '+ Pop-up store' }))
    const dialog = await screen.findByRole('dialog', { name: '+ Pop-up store' })
    await user.type(within(dialog).getByLabelText('Name'), 'HTN Pop-up · new')
    await user.type(within(dialog).getByLabelText('Venue / host'), 'City Hall grounds')
    fireEvent.change(within(dialog).getByLabelText('Starts'), { target: { value: '2026-11-10T08:00' } })
    fireEvent.change(within(dialog).getByLabelText('Ends'), { target: { value: '2026-11-10T16:00' } })
    await user.selectOptions(within(dialog).getByLabelText('Committee running this pop-up'), 'c1')
    await user.click(within(dialog).getByRole('button', { name: 'Create pop-up' }))

    expect(createdBody).toMatchObject({ name: 'HTN Pop-up · new', ownerCommitteeId: 'c1', venueText: 'City Hall grounds' })

    const allocDialog = await screen.findByRole('dialog', { name: 'Stock to bring' })
    await user.type(within(allocDialog).getByLabelText(/HTN Con Tee \(S\)/), '4')
    await user.click(within(allocDialog).getByRole('button', { name: 'Save' }))

    expect(allocatedBody).toEqual({ lines: [{ variantId: 'v1', qty: 4 }] })
  })

  it('closes the store, then records a count with returned stock and cash counted', async () => {
    signInAs(boardMe)
    mockBase([popUp()])
    let closed = false
    let countedBody: Record<string, unknown> | null = null
    server.use(
      http.post('/api/pop-ups/pu1/close', () => {
        closed = true
        return HttpResponse.json(popUp({ status: 'NeedsCount' }))
      }),
      http.post('/api/pop-ups/pu1/count', async ({ request }) => {
        countedBody = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(closedMissingPopUp())
      }),
    )
    const user = userEvent.setup()
    renderPage()

    await screen.findAllByText('HTN Pop-up · SM Megamall')
    await user.click(screen.getByRole('button', { name: 'Close store & count' }))
    expect(closed).toBe(true)

    const dialog = await screen.findByRole('dialog', { name: 'Close store & count' })
    const returned = within(dialog).getByLabelText(/HTN Con Tee \(S\) — returned/)
    fireEvent.change(returned, { target: { value: '5' } })
    const cash = within(dialog).getByLabelText('Cash counted')
    fireEvent.change(cash, { target: { value: '1000' } })
    await user.click(within(dialog).getByRole('button', { name: 'Record count' }))

    expect(countedBody).toEqual({ returned: [{ variantId: 'v1', qtyReturned: 5 }], cashCounted: 1000 })
  })

  it('renders the leftover inventory table columns', async () => {
    signInAs(boardMe)
    mockBase([popUp()], [leftoverVariant()])
    const user = userEvent.setup()
    renderPage()

    await screen.findAllByText('HTN Pop-up · SM Megamall')
    await user.click(screen.getByRole('radio', { name: /Leftover inventory/ }))
    expect(await screen.findByText('HTN Con Tee (S)')).toBeInTheDocument()
    expect(screen.getByText('HTN Con 2026')).toBeInTheDocument()
    expect(screen.getByText('₱450')).toBeInTheDocument()
  })
})
