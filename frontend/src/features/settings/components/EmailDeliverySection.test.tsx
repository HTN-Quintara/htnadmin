import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { boardMe, goonMe, problem, server, signInAs } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { SettingsPage } from '../SettingsPage'
import type { EmailSettings } from '../schemas'

const saved: EmailSettings = {
  mode: 'Sandbox',
  inboxId: '123456',
  fromEmail: 'hello@hackthenorth.ph',
  fromName: 'HackTheNorth.PH',
  replyTo: null,
  tokenSet: true,
  tokenLast4: 'a9f2',
  tokenUnreadable: false,
  source: 'Database',
  updatedAt: '2026-10-30T08:00:00Z',
}

function mockEmail(settings: EmailSettings = saved) {
  server.use(http.get('/api/settings/email', () => HttpResponse.json(settings)))
}

const path = '/settings?section=email'

describe('Email delivery', () => {
  it('shows the status and the masked saved token without prefilling it', async () => {
    signInAs(boardMe)
    mockEmail()
    renderWithProviders(<SettingsPage />, path)
    expect(await screen.findByText('Saved ••••a9f2')).toBeInTheDocument()
    expect(screen.getByText('Configured from settings')).toBeInTheDocument()
    expect(screen.queryByLabelText(/API token/)).not.toBeInTheDocument()
    expect(screen.getByLabelText('Inbox ID')).toHaveValue('123456')
  })

  it('hides the inbox ID in Live mode', async () => {
    signInAs(boardMe)
    mockEmail()
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, path)
    await user.click(await screen.findByRole('radio', { name: /Live/ }))
    expect(screen.queryByLabelText('Inbox ID')).not.toBeInTheDocument()
  })

  it('requires the inbox ID in Sandbox mode', async () => {
    signInAs(boardMe)
    mockEmail({ ...saved, inboxId: null })
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, path)
    await user.type(await screen.findByLabelText('From name'), ' Team')
    await user.click(screen.getByRole('button', { name: 'Save email delivery' }))
    expect(await screen.findByText('Enter the sandbox inbox ID.')).toBeInTheDocument()
    expect(screen.getByLabelText('Inbox ID')).toHaveAttribute('aria-invalid', 'true')
  })

  it('saves with apiToken null when the token is untouched', async () => {
    signInAs(boardMe)
    mockEmail()
    let body: unknown
    server.use(
      http.put('/api/settings/email', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ ...saved, fromName: 'HTN Team' })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, path)
    const name = await screen.findByLabelText('From name')
    await user.clear(name)
    await user.type(name, 'HTN Team')
    await user.click(screen.getByRole('button', { name: 'Save email delivery' }))
    await waitFor(() =>
      expect(body).toEqual({
        mode: 'Sandbox',
        inboxId: '123456',
        fromEmail: 'hello@hackthenorth.ph',
        fromName: 'HTN Team',
        replyTo: null,
        apiToken: null,
        clearToken: false,
      }),
    )
  })

  it('sends a test email to the signed-in user by default', async () => {
    signInAs(boardMe)
    mockEmail()
    let body: unknown
    server.use(
      http.post('/api/settings/email/test', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json({ to: 'a***@hackthenorth.ph' }, { status: 202 })
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, path)
    expect(await screen.findByLabelText('Recipient')).toHaveValue(boardMe.email)
    await user.click(screen.getByRole('button', { name: 'Send test email' }))
    expect(await screen.findByText('Test email queued for a***@hackthenorth.ph.')).toBeInTheDocument()
    expect(body).toEqual({ to: boardMe.email })
  })

  it('warns when the saved token cannot be decrypted', async () => {
    signInAs(boardMe)
    mockEmail({ ...saved, tokenUnreadable: true })
    renderWithProviders(<SettingsPage />, path)
    expect(await screen.findByRole('alert')).toHaveTextContent("The saved API token can't be decrypted (encryption keys changed). Enter the token again.")
  })

  it('shows the server error under the recipient field', async () => {
    signInAs(boardMe)
    mockEmail()
    server.use(
      http.post('/api/settings/email/test', () =>
        problem(400, 'Validation failed', undefined, { errors: { to: ['Use your own email or an address on the org domain.'] } }),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, path)
    const to = await screen.findByLabelText('Recipient')
    await user.clear(to)
    await user.type(to, 'someone@gmail.com')
    await user.click(screen.getByRole('button', { name: 'Send test email' }))
    expect(await screen.findByText('Use your own email or an address on the org domain.')).toBeInTheDocument()
    expect(to).toHaveAttribute('aria-invalid', 'true')
  })

  it('explains the rate limit on 429', async () => {
    signInAs(boardMe)
    mockEmail()
    server.use(http.post('/api/settings/email/test', () => problem(429, 'Too Many Requests')))
    const user = userEvent.setup()
    renderWithProviders(<SettingsPage />, path)
    await user.click(await screen.findByRole('button', { name: 'Send test email' }))
    expect(await screen.findByText('Too many test emails. Try again later.')).toBeInTheDocument()
  })

  it('is hidden without Settings.Manage', async () => {
    signInAs(goonMe)
    renderWithProviders(<SettingsPage />, path)
    const nav = await screen.findByRole('navigation', { name: 'Settings sections' })
    expect(within(nav).queryByRole('link', { name: 'Email delivery' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Email delivery' })).not.toBeInTheDocument()
  })
})
