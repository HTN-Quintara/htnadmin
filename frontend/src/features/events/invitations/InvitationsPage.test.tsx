import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { RequirePolicy } from '@/app/auth/RequirePolicy'
import { Policies } from '@/app/auth/policies'
import { orgsList } from '@/test/fixtures/directory'
import {
  invitation,
  invitationDetail,
  invitationTemplate,
  invitationsList,
  sentInvitation,
  unverifiedInvitation,
} from '@/test/fixtures/invitations'
import { boardMe, goonMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { InvitationsPage } from './InvitationsPage'

function mockInvitations(list: ReturnType<typeof invitationsList> = invitationsList()) {
  let current = list
  server.use(
    http.get('/api/events/:eventId/invitations', ({ request }) => {
      const url = new URL(request.url)
      const status = url.searchParams.get('status')
      const rows = status ? current.filter((i) => i.status === status) : current
      return HttpResponse.json(rows)
    }),
  )
  return (next: ReturnType<typeof invitationsList>) => { current = next }
}

function mockTemplate() {
  server.use(http.get('/api/events/:eventId/invitations/template', () => HttpResponse.json(invitationTemplate())))
}

function mockDirectory() {
  server.use(http.get('/api/directory', () => HttpResponse.json(orgsList())))
}

function renderPage() {
  return renderRoutes([{ path: '/events/:eventId/invitations', element: <InvitationsPage /> }], '/events/e1/invitations')
}

describe('Invitations (frame 19)', () => {
  it('lists invitations and filters by status tab', async () => {
    signInAs(boardMe)
    mockInvitations()
    mockTemplate()
    renderPage()

    const table1 = await screen.findByRole('table')
    expect(within(table1).getByText('Department of Information and Communications Technology')).toBeInTheDocument()
    expect(within(table1).getByText('CloudCorp PH')).toBeInTheDocument()

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Sent' }))
    const table2 = await screen.findByRole('table')
    expect(await within(table2).findByText('CloudCorp PH')).toBeInTheDocument()
    expect(within(table2).queryByText('Department of Information and Communications Technology')).not.toBeInTheDocument()
  })

  it('picks organizations from the directory and creates invitations', async () => {
    signInAs(boardMe)
    const setList = mockInvitations([])
    mockTemplate()
    mockDirectory()
    const orgsById = new Map(orgsList().map((o) => [o.id, o]))
    server.use(
      http.post('/api/events/:eventId/invitations', async ({ request }) => {
        const body = (await request.json()) as { orgIds: string[] }
        setList(
          body.orgIds.map((id) =>
            invitation({ id: `new-${id}`, directoryOrgId: id, orgName: orgsById.get(id)?.name, orgKind: orgsById.get(id)?.kind }),
          ),
        )
        return HttpResponse.json({ created: body.orgIds, skipped: [] })
      }),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: '+ Organizations' }))
    const dialog = await screen.findByRole('dialog', { name: 'Pick organizations' })
    await user.click(within(dialog).getByRole('checkbox', { name: /CloudCorp PH/ }))
    await user.click(within(dialog).getByRole('button', { name: /Add 1 organization/ }))

    const table = await screen.findByRole('table')
    expect(await within(table).findByText('CloudCorp PH')).toBeInTheDocument()
  })

  it('shows "Verify first" for an org whose contact is not verified, and hides Send for it', async () => {
    signInAs(boardMe)
    mockInvitations([unverifiedInvitation()])
    mockTemplate()
    renderPage()

    expect(await screen.findByRole('link', { name: 'Verify first →' })).toBeInTheDocument()
    const sendButton = screen.getByRole('button', { name: 'Send' })
    expect(sendButton).toBeDisabled()
  })

  it('sends the letter for a verified, not-sent organization and the row becomes Sent', async () => {
    signInAs(boardMe)
    const setList = mockInvitations([invitation()])
    mockTemplate()
    server.use(
      http.post('/api/events/:eventId/invitations/:id/send', () => {
        setList([invitation({ status: 'Sent', sentAt: '2026-10-27T00:00:00Z' })])
        return HttpResponse.json(invitationDetail())
      }),
    )
    const user = userEvent.setup()
    renderPage()

    const table = await screen.findByRole('table')
    const sendButton = within(table).getByRole('button', { name: 'Send' })
    expect(sendButton).toBeEnabled()
    await user.click(sendButton)

    expect(await within(table).findByText('Sent to verified contact')).toBeInTheDocument()
  })

  it('only offers the valid next statuses for the manual status control', async () => {
    signInAs(boardMe)
    mockInvitations([sentInvitation()])
    mockTemplate()
    renderPage()

    const table = await screen.findByRole('table')
    await within(table).findByText('CloudCorp PH')
    const select = within(table).getByLabelText('Move CloudCorp PH to')
    const options = within(select).getAllByRole('option').map((o) => o.textContent)
    expect(options).toEqual(['Move to…', 'Opened', 'Replied', 'Confirmed', 'Declined', 'Bounced'])
  })

  it('denies a non-board user (no committee-lead exception, unlike Directory)', async () => {
    signInAs(goonMe)
    mockInvitations()
    mockTemplate()
    renderRoutes(
      [
        {
          path: '/events/:eventId/invitations',
          element: <RequirePolicy policy={Policies.InvitationsManage} />,
          children: [{ index: true, element: <InvitationsPage /> }],
        },
      ],
      '/events/e1/invitations',
    )

    expect(await screen.findByText('You do not have access to this page')).toBeInTheDocument()
    expect(screen.queryByText('CloudCorp PH')).not.toBeInTheDocument()
  })
})
