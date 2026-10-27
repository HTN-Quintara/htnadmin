import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { RequirePolicy } from '@/app/auth/RequirePolicy'
import { Policies } from '@/app/auth/policies'
import { cloudcorpContact, cloudcorpOrg, historyEntry, orgDetail, orgsList } from '@/test/fixtures/directory'
import { boardMe, committeeLeadMe, goonMe, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'
import { DirectoryPage } from './DirectoryPage'

function mockList() {
  server.use(
    http.get('/api/directory', ({ request }) => {
      const url = new URL(request.url)
      const kind = url.searchParams.get('kind')
      const needsCheck = url.searchParams.get('needsCheck')
      let orgs = orgsList()
      if (kind) orgs = orgs.filter((o) => o.kind === kind)
      if (needsCheck !== null) orgs = orgs.filter((o) => o.needsCheck === (needsCheck === 'true'))
      return HttpResponse.json(orgs)
    }),
  )
}

/** Returns a setter so a later mutation handler can update what the GET detail route serves next. */
function mockDetail(id: string, detail: ReturnType<typeof orgDetail>) {
  let current = detail
  server.use(http.get(`/api/directory/${id}`, () => HttpResponse.json(current)))
  return (next: ReturnType<typeof orgDetail>) => { current = next }
}

function renderPage() {
  return renderRoutes([{ path: '/directory', element: <DirectoryPage /> }], '/directory')
}

describe('Directory (frame 18)', () => {
  it('lists organizations from the directory', async () => {
    signInAs(boardMe)
    mockList()
    renderPage()

    expect(await screen.findByText('Department of Information and Communications Technology')).toBeInTheDocument()
    expect(screen.getByText('CloudCorp PH')).toBeInTheDocument()
    expect(screen.getByText('University of the Philippines Diliman')).toBeInTheDocument()
  })

  it('filters by kind and by needs-check', async () => {
    signInAs(boardMe)
    mockList()
    const user = userEvent.setup()
    renderPage()

    await screen.findByText('CloudCorp PH')
    await user.click(screen.getByRole('button', { name: 'Government' }))
    expect(await screen.findByText('Department of Information and Communications Technology')).toBeInTheDocument()
    expect(screen.queryByText('CloudCorp PH')).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Needs check' }))
    expect(await screen.findByText('University of the Philippines Diliman')).toBeInTheDocument()
    expect(screen.queryByText('Department of Information and Communications Technology')).not.toBeInTheDocument()
  })

  it('hides manage controls for a read-only (committee lead) persona, but still renders', async () => {
    signInAs(committeeLeadMe)
    mockList()
    renderPage()

    expect(await screen.findByText('CloudCorp PH')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Organization' })).not.toBeInTheDocument()
  })

  it('denies a user with neither Directory policy', async () => {
    signInAs(goonMe)
    mockList()
    renderRoutes(
      [{ path: '/directory', element: <RequirePolicy policy={Policies.DirectoryView} />, children: [{ index: true, element: <DirectoryPage /> }] }],
      '/directory',
    )
    expect(await screen.findByText('You do not have access to this page')).toBeInTheDocument()
    expect(screen.queryByText('CloudCorp PH')).not.toBeInTheDocument()
  })

  it('opens the org detail aside, submits "Head changed", and reflects the new holder and history', async () => {
    signInAs(boardMe)
    mockList()
    const cc = cloudcorpOrg()
    const setDetail = mockDetail(cc.id, orgDetail({ org: cc, contacts: [cloudcorpContact()], history: [] }))
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: 'CloudCorp PH' }))
    expect(await screen.findByRole('heading', { name: 'CloudCorp PH' })).toBeInTheDocument()
    expect(screen.getByText('Kai Cruz')).toBeInTheDocument()

    const updatedContact = cloudcorpContact({ holderName: 'Nina Santos', email: 'nina.santos@cloudcorp.ph', verifiedAt: '2026-10-27T00:00:00Z' })
    const updatedHistory = [historyEntry({ id: 'h2', contactId: 'c2', kind: 'HolderChanged', priorPosition: 'Partnerships', priorHolderName: 'Kai Cruz', at: '2026-10-27T00:00:00Z' })]
    const updatedDetail = orgDetail({ org: cc, contacts: [updatedContact], history: updatedHistory })
    server.use(
      http.post(`/api/directory/${cc.id}/contacts/c2/head-changed`, () => {
        setDetail(updatedDetail)
        return HttpResponse.json(updatedDetail)
      }),
    )

    await user.click(screen.getByRole('button', { name: 'Head changed' }))
    const dialog = await screen.findByRole('dialog', { name: 'Head changed' })
    const holderInput = within(dialog).getByLabelText('Holder name')
    await user.clear(holderInput)
    await user.type(holderInput, 'Nina Santos')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Nina Santos')).toBeInTheDocument()
    expect(screen.getByText('Head changed', { selector: 'span.font-medium' })).toBeInTheDocument()
    expect(screen.getByText('was: Partnerships — Kai Cruz')).toBeInTheDocument()
  })

  it('opens the add-organization dialog for board', async () => {
    signInAs(boardMe)
    mockList()
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByRole('button', { name: '+ Organization' }))
    expect(await screen.findByRole('dialog', { name: 'Add organization' })).toBeInTheDocument()
  })
})
