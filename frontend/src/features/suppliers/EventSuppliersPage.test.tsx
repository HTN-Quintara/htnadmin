import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { eventSupplierHire, suppliersList } from '@/test/fixtures/suppliers'
import { boardMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { EventSuppliersPage } from './EventSuppliersPage'

function mockHires(hires: ReturnType<typeof eventSupplierHire>[]) {
  let current = hires
  server.use(http.get('/api/events/e1/suppliers', () => HttpResponse.json(current)))
  return (next: typeof hires) => { current = next }
}

function mockSupporting() {
  server.use(
    http.get('/api/suppliers', () => HttpResponse.json(suppliersList(true))),
    http.get('/api/finance/categories', () => HttpResponse.json([{ id: 'cat1', code: 'VEN', name: 'Venue', kind: 'Expense', isSystem: false, isActive: true }])),
    http.get('/api/events/e1/committees', () =>
      HttpResponse.json({ canCreateCommittee: true, canManageVolunteers: true, committees: [{ id: 'com1', name: 'Logistics', sortOrder: 0, isActive: true, canManage: true, openTasks: 0, peopleCount: 0, lead: null, members: [], volunteers: [] }], pool: { people: [], volunteers: [] } }),
    ),
  )
}

function renderPage() {
  return renderRoutes([{ path: '/events/:eventId/suppliers', element: <EventSuppliersPage /> }], '/events/e1/suppliers')
}

describe('Event suppliers (hire management)', () => {
  it('hires a supplier for the event', async () => {
    signInAs(boardMe)
    mockHires([])
    mockSupporting()
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('No suppliers hired yet')
    await user.click(screen.getByRole('button', { name: '+ Hire supplier' }))
    const dialog = await screen.findByRole('dialog', { name: 'Hire a supplier' })

    server.use(
      http.post('/api/events/e1/suppliers', () => HttpResponse.json(eventSupplierHire(true, { status: 'Quotation' }))),
    )

    await user.selectOptions(screen.getByLabelText('Supplier'), 's1')
    await user.selectOptions(screen.getByLabelText('Budget category'), 'cat1')
    await user.selectOptions(screen.getByLabelText('Owning committee'), 'com1')
    await user.click(within(dialog).getByRole('button', { name: 'Hire supplier' }))

    expect(await screen.findByText('Supplier hired.')).toBeInTheDocument()
  })

  it('offers only the one valid next status step, and allows delete only while Quotation', async () => {
    signInAs(boardMe)
    mockHires([eventSupplierHire(true, { status: 'Quotation' })])
    mockSupporting()
    renderPage()

    expect(await screen.findByRole('button', { name: 'Move to Booked' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Move to Deposit paid' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Move to Fully paid' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Void' })).not.toBeInTheDocument()
  })

  it('offers Void instead of Delete once past Quotation', async () => {
    signInAs(boardMe)
    mockHires([eventSupplierHire(true, { status: 'Booked' })])
    mockSupporting()
    renderPage()

    expect(await screen.findByRole('button', { name: 'Move to Deposit paid' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Void' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
  })
})
