import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { partnersPage, sponsorDetail, sponsorsPage } from '@/test/fixtures/sponsors'
import { boardMe, server, signInAs } from '@/test/server'
import { renderWithProviders } from '@/test/render'
import { SponsorsView } from './components/SponsorsView'
import { PartnersView } from './components/PartnersView'

const EVENT = 'e1'

function sponsorsApi() {
  server.use(
    http.get(`/api/events/${EVENT}/sponsors`, () => HttpResponse.json(sponsorsPage())),
    http.get(`/api/events/${EVENT}/sponsors/:sid`, () => HttpResponse.json(sponsorDetail())),
  )
}

function partnersApi() {
  server.use(http.get(`/api/events/${EVENT}/partners`, () => HttpResponse.json(partnersPage())))
}

describe('Sponsors (frame 13)', () => {
  it('shows the KPIs, the tiers and the sponsors table', async () => {
    signInAs(boardMe)
    sponsorsApi()
    renderWithProviders(<SponsorsView eventId={EVENT} />)

    expect(await screen.findByText('Cash committed')).toBeInTheDocument()
    expect(screen.getByText('Lanyard & ID')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'CloudCorp PH' })).toBeInTheDocument()
  })

  it('filters sponsors by contribution', async () => {
    signInAs(boardMe)
    server.use(
      http.get(`/api/events/${EVENT}/sponsors`, () =>
        HttpResponse.json(
          sponsorsPage({
            sponsors: [
              { ...sponsorsPage().sponsors[0] },
              {
                ...sponsorsPage().sponsors[0],
                id: 's2',
                name: 'ToteWorks',
                contributionType: 'In-kind',
                cashAmount: null,
                inKindDescription: '400 tote bags',
                amountText: '400 tote bags',
              },
            ],
          }),
        ),
      ),
    )
    const user = userEvent.setup()
    renderWithProviders(<SponsorsView eventId={EVENT} />)

    expect(await screen.findByRole('button', { name: 'ToteWorks' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'In-kind' }))
    expect(screen.queryByRole('button', { name: 'CloudCorp PH' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'ToteWorks' })).toBeInTheDocument()
  })

  it('opens the add-sponsor dialog', async () => {
    signInAs(boardMe)
    sponsorsApi()
    const user = userEvent.setup()
    renderWithProviders(<SponsorsView eventId={EVENT} />)

    await user.click(await screen.findByRole('button', { name: '+ Sponsor' }))
    expect(await screen.findByRole('dialog', { name: 'Add sponsor' })).toBeInTheDocument()
  })
})

describe('Partners (frame 13)', () => {
  it('shows partners, the threshold rule and the unmatched queue', async () => {
    signInAs(boardMe)
    partnersApi()
    renderWithProviders(<PartnersView eventId={EVENT} />)

    expect(await screen.findByText('DICT')).toBeInTheDocument()
    expect(screen.getByText('Partner rule · this event')).toBeInTheDocument()
    expect(screen.getByText(/Review 1 unmatched names/i)).toBeInTheDocument()
    // The threshold stepper shows the current value in its live-region output.
    expect(screen.getByText('20')).toBeInTheDocument()
  })

  it('opens the tag-partner dialog', async () => {
    signInAs(boardMe)
    partnersApi()
    const user = userEvent.setup()
    renderWithProviders(<PartnersView eventId={EVENT} />)

    await user.click(await screen.findByRole('button', { name: 'Tag a partner' }))
    expect(await screen.findByRole('dialog', { name: 'Tag a partner' })).toBeInTheDocument()
  })
})
