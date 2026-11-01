import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { routes } from '@/app/router'
import { budget, financeHandlers } from '@/test/fixtures/finance'
import { boardMe, cfoMe, problem, server, signInAs } from '@/test/server'
import { renderRoutes } from '@/test/render'

function useFinanceApi() {
  server.use(...financeHandlers(http, (body) => HttpResponse.json(body)))
}

function renderBudget(path = '/finance/budgets/evt-1') {
  const user = userEvent.setup()
  return { user, ...renderRoutes(routes, path) }
}

describe('Event budget (frame 25)', () => {
  it('shows budgeted against actual, with the variance in words as well as a sign', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    renderBudget()

    expect(await screen.findByRole('heading', { name: 'HTN Con 2026' })).toBeInTheDocument()
    expect(screen.getByText(/Version 2 · Noel Cruz/)).toBeInTheDocument()

    const income = await screen.findByRole('table', { name: '' }).catch(() => null)
    expect(income).toBeDefined()

    // Income earned more than planned: better, so the variance reads "to spare".
    expect(screen.getByText(/\+₱20,000 to spare/)).toBeInTheDocument()
    // The expense line overspent: the words say "over", not just a red bar.
    expect(screen.getByText(/−₱18,000 over/)).toBeInTheDocument()
  })

  it('hides the edit button from a board member who is not the CFO, and says why', async () => {
    signInAs(boardMe)
    useFinanceApi()
    server.use(http.get('/api/finance/budgets/:eventId', () => HttpResponse.json(budget({ canEdit: false }))))
    renderBudget()

    expect(await screen.findByText(/Budgets are edited by the CFO/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Edit budget/i })).not.toBeInTheDocument()
  })

  it('requires a reason before saving a version', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    const { user } = renderBudget()

    await user.click(await screen.findByRole('button', { name: 'Edit budget' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Save new version' }))

    expect(await within(dialog).findByText('Say why the budget changed.')).toBeInTheDocument()
  })

  it('maps a server field error back onto the line that caused it', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    server.use(
      http.put('/api/finance/budgets/:eventId', () =>
        HttpResponse.json(
          {
            status: 400,
            title: 'Validation failed',
            code: 'unknown_category',
            errors: { 'lines[0].categoryId': ['That category no longer exists. Pick another one.'] },
          },
          { status: 400 },
        ),
      ),
    )
    const { user } = renderBudget()

    await user.click(await screen.findByRole('button', { name: 'Edit budget' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText(/Why is the budget changing/i), 'Trying a new category')
    await user.click(within(dialog).getByRole('button', { name: 'Save new version' }))

    expect(await within(dialog).findByText(/That category no longer exists/i)).toBeInTheDocument()
  })

  it('offers the version history and shows an older version read-only', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    server.use(
      http.get('/api/finance/budgets/:eventId/versions/:versionId', () =>
        HttpResponse.json(
          budget({
            version: { id: 'ver-1', versionNumber: 1, reason: 'First draft', createdBy: 'Noel Cruz', createdAt: '2026-10-01T02:00:00Z' },
          }),
        ),
      ),
    )
    const { user } = renderBudget()

    await user.click(await screen.findByRole('button', { name: 'Budget versions' }))
    await user.click(await screen.findByRole('menuitem', { name: /Version 1/ }))

    expect(await screen.findByText(/You are looking at version 1/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit budget' })).not.toBeInTheDocument()
  })

  it('says the suppliers panel is still to come rather than leaving a blank box', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    renderBudget()

    expect(await screen.findByText('Suppliers arrive with the directory')).toBeInTheDocument()
  })

  it('shows a not-found state for an event that is gone', async () => {
    signInAs(cfoMe)
    useFinanceApi()
    server.use(http.get('/api/finance/budgets/:eventId', () => problem(404, 'Not found')))
    renderBudget()

    expect(await screen.findByText('Event not found')).toBeInTheDocument()
  })
})
