import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { speakerFormState, speakerLanding } from '@/test/fixtures/speakers'
import { server } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { SpeakerFormPage } from './SpeakerFormPage'

const TOKEN = 'tok123'

function render() {
  return renderRoutes([{ path: '/s/:token', element: <SpeakerFormPage /> }], `/s/${TOKEN}`)
}

describe('Public speaker confirmation (frame 23)', () => {
  it('shows the verify step with the masked email', async () => {
    server.use(
      http.get(`/api/public/speaker-links/${TOKEN}`, () => HttpResponse.json(speakerLanding())),
      http.post(`/api/public/speaker-links/${TOKEN}/code`, () => new HttpResponse(null, { status: 204 })),
    )
    render()
    expect(await screen.findByText(/l•••••@dict\.gov\.ph/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Verify and continue' })).toBeInTheDocument()
  })

  it('shows "this link no longer works" for a dead token', async () => {
    server.use(http.get(`/api/public/speaker-links/${TOKEN}`, () => HttpResponse.json({ code: 'link_expired' }, { status: 410 })))
    render()
    expect(await screen.findByText('This link no longer works')).toBeInTheDocument()
  })

  it('verifies the code and moves to the adaptive form', async () => {
    server.use(
      http.get(`/api/public/speaker-links/${TOKEN}`, () => HttpResponse.json(speakerLanding())),
      http.post(`/api/public/speaker-links/${TOKEN}/code`, () => new HttpResponse(null, { status: 204 })),
      http.post(`/api/public/speaker-links/${TOKEN}/verify`, () =>
        HttpResponse.json({ sessionToken: 'sess1', sessionExpiresAt: '2026-11-01T00:00:00+08:00', form: speakerFormState() })),
    )
    const user = userEvent.setup()
    render()
    await screen.findByRole('button', { name: 'Verify and continue' })
    await user.type(screen.getByLabelText('Verification code'), '482913')
    await user.click(screen.getByRole('button', { name: 'Verify and continue' }))

    expect(await screen.findByText(/Thank you for speaking, Lara/)).toBeInTheDocument()
    expect(screen.getByText('Your flight')).toBeInTheDocument()
    expect(screen.getByLabelText(/Full name as on your government ID/)).toBeInTheDocument()
  })

  it('a wrong code shows an error without advancing', async () => {
    server.use(
      http.get(`/api/public/speaker-links/${TOKEN}`, () => HttpResponse.json(speakerLanding())),
      http.post(`/api/public/speaker-links/${TOKEN}/code`, () => new HttpResponse(null, { status: 204 })),
      http.post(`/api/public/speaker-links/${TOKEN}/verify`, () => HttpResponse.json({ code: 'code_invalid' }, { status: 400 })),
    )
    const user = userEvent.setup()
    render()
    await screen.findByRole('button', { name: 'Verify and continue' })
    await user.type(screen.getByLabelText('Verification code'), '000000')
    await user.click(screen.getByRole('button', { name: 'Verify and continue' }))
    await waitFor(() => expect(screen.getByText(/code is not right/i)).toBeInTheDocument())
  })
})
