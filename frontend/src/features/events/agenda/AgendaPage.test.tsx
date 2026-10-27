import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { agenda } from '@/test/fixtures/agenda'
import { boardMe, goonMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { AgendaPage } from './AgendaPage'

function agendaApi(over: Record<string, unknown> = {}) {
  server.use(
    http.get('/api/events/:eventId/agenda', () => HttpResponse.json(agenda(over))),
    http.get('/api/events/:eventId/speakers', () => HttpResponse.json([])),
  )
}

function render() {
  return renderRoutes([{ path: '/events/:eventId/agenda', element: <AgendaPage /> }], '/events/e1/agenda')
}

describe('Agenda (frame 16)', () => {
  it('shows the day heading, time slots, sessions and a Tentative badge', async () => {
    signInAs(boardMe)
    agendaApi()
    render()
    expect(await screen.findByRole('heading', { name: 'Saturday, Nov 22' })).toBeInTheDocument()
    expect(screen.getByText('9:15 AM')).toBeInTheDocument()
    expect(screen.getByText('The state of Philippine cybersecurity')).toBeInTheDocument()
    expect(screen.getByText('Tentative')).toBeInTheDocument()
    expect(screen.getByText(/1 speakers still tentative/)).toBeInTheDocument()
  })

  it('lets a manager add a session', async () => {
    signInAs(boardMe)
    agendaApi()
    const user = userEvent.setup()
    render()
    await user.click(await screen.findByRole('button', { name: '+ Session' }))
    expect(await screen.findByRole('dialog', { name: 'Add session' })).toBeInTheDocument()
  })

  it('hides the write controls when the caller cannot manage', async () => {
    signInAs(goonMe)
    agendaApi({ canManage: false })
    render()
    expect(await screen.findByText('The state of Philippine cybersecurity')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Session' })).not.toBeInTheDocument()
  })
})
