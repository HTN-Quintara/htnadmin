import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { leadsBoard } from '@/test/fixtures/leads'
import { boardMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { LeadsPage } from './LeadsPage'

function leadsApi() {
  server.use(http.get('/api/events/:eventId/leads', () => HttpResponse.json(leadsBoard())))
}

describe('Leads (frame 17)', () => {
  it('shows the five pipeline columns, stats and a card', async () => {
    signInAs(boardMe)
    leadsApi()
    renderRoutes([{ path: '/events/:eventId/leads', element: <LeadsPage /> }], '/events/e1/leads')

    expect(await screen.findByText('Globe Business')).toBeInTheDocument()
    expect(screen.getByText('In pipeline')).toBeInTheDocument()
    for (const name of ['Prospect', 'Contacted', 'In talks', 'Confirmed', 'Declined'])
      expect(screen.getByRole('region', { name })).toBeInTheDocument()
  })

  it('shows the Open sponsor entry CTA on a confirmed sponsor lead', async () => {
    signInAs(boardMe)
    leadsApi()
    renderRoutes([{ path: '/events/:eventId/leads', element: <LeadsPage /> }], '/events/e1/leads')
    expect(await screen.findByRole('link', { name: 'Open sponsor entry' })).toBeInTheDocument()
  })

  it('opens the add-lead dialog', async () => {
    signInAs(boardMe)
    leadsApi()
    const user = userEvent.setup()
    renderRoutes([{ path: '/events/:eventId/leads', element: <LeadsPage /> }], '/events/e1/leads')
    await user.click(await screen.findByRole('button', { name: '+ Sponsor lead' }))
    expect(await screen.findByRole('dialog', { name: 'Add sponsor lead' })).toBeInTheDocument()
  })
})
