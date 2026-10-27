import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { speakerDetail, speakersList } from '@/test/fixtures/speakers'
import { boardMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { SpeakersPage } from './SpeakersPage'

const EVENT = 'e1'

function speakersApi() {
  server.use(
    http.get(`/api/events/${EVENT}/agenda/speakers`, () => HttpResponse.json(speakersList())),
    http.get(`/api/events/${EVENT}/agenda/speakers/:sid`, () => HttpResponse.json(speakerDetail())),
  )
}

function render() {
  return renderRoutes([{ path: '/events/:eventId/agenda/speakers', element: <SpeakersPage /> }], '/events/e1/agenda/speakers')
}

describe('Speakers (frame 22)', () => {
  it('shows the travel mode banner, KPIs and the speaker row', async () => {
    signInAs(boardMe)
    speakersApi()
    render()
    expect(await screen.findByText('Dr. Lara Gomez')).toBeInTheDocument()
    expect(screen.getByText('Plane')).toBeInTheDocument()
    expect(screen.getByText('Confirmed speakers')).toBeInTheDocument()
    expect(screen.getByText('Opened')).toBeInTheDocument()
  })

  it('opens the speaker drawer with travel, plus-one and the access log', async () => {
    signInAs(boardMe)
    speakersApi()
    const user = userEvent.setup()
    render()
    await user.click(await screen.findByRole('button', { name: /Dr\. Lara Gomez/ }))
    expect(await screen.findByText('Davao')).toBeInTheDocument()
    expect(screen.getByText(/SPK-GOMEZ-7Q4X/)).toBeInTheDocument()
    expect(screen.getByText('Form submitted')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Approve photo' })).toBeInTheDocument()
  })
})
