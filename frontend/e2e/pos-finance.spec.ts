/**
 * Finance posting (CLAUDE.md's third critical flow), and the merch POS sale that belongs to a later phase.
 * The POS screen is still a "Coming soon" placeholder, so that one stays a fixme until the merch slice builds it.
 */
import { expect, test, type Page } from '@playwright/test'
import { signInAsBoard, uniqueLetters } from './support/seed'

test.describe('POS and finance', () => {
  test.fixme('POS sale with a discount posts revenue to the ledger', async () => {})

  /**
   * The month-end flow, end to end: post an entry with its receipt, be refused the close while an account is
   * unreconciled, reconcile, close, then be refused an entry dated inside the closed month. The seeded dev account
   * (alex@) holds the CFO flag, which is what makes all of this reachable.
   */
  test('finance posting: a month cannot close while unbalanced', async ({ page }) => {
    await signInAsBoard(page)
    await openFinance(page)

    await expect(page.getByRole('heading', { name: 'Finance', exact: true })).toBeVisible()

    // The dev database keeps whatever an earlier run left behind, so start from a known state: if this month is
    // already closed, reopen it. Reopening the latest closed month with a reason is a real CFO action, so this
    // exercises the product rather than working around it.
    await page.getByRole('radio', { name: 'Balancing' }).click()
    // Wait for the accounts to arrive: the Reopen button only exists once the period is known, and probing for it
    // too early would silently skip the reset.
    await expect(page.getByRole('table')).toBeVisible()
    const reopen = page.getByRole('button', { name: 'Reopen month' })
    if (await reopen.isVisible()) {
      await reopen.click()
      const reopenDialog = page.getByRole('alertdialog')
      await reopenDialog.getByLabel(/Why are you reopening it/i).fill('End-to-end test rerun')
      await reopenDialog.getByRole('button', { name: 'Reopen month' }).click()
      await expect(reopenDialog).toBeHidden()
    }

    // ---- Post an entry, with the receipt every hand-entered row needs ------------------
    await page.getByRole('radio', { name: 'Ledger' }).click()
    await page.getByRole('button', { name: 'New entry' }).click()

    const dialog = page.getByRole('dialog')
    await expect(dialog.getByText(/posts straight away/i)).toBeVisible()
    const description = `E2E venue deposit ${uniqueLetters()}`
    await dialog.getByLabel('Fund').selectOption({ label: 'Organization' })
    await dialog.getByLabel('Account').selectOption({ index: 1 })
    await dialog.getByLabel('Category').selectOption({ index: 1 })
    await dialog.getByLabel('Amount (₱)').fill('1500.50')
    await dialog.getByLabel('Reference').fill('OR-E2E-1')
    await dialog.getByLabel('Description').fill(description)
    // A 1x1 PNG is a real PNG: the server checks the bytes, never the file name.
    await dialog.locator('input[type="file"]').setInputFiles({
      name: 'receipt.png',
      mimeType: 'image/png',
      buffer: Buffer.from(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==',
        'base64',
      ),
    })
    await dialog.getByRole('button', { name: 'Post entry' }).click()

    const row = page.getByRole('row').filter({ hasText: description })
    await expect(row).toBeVisible()
    await expect(row.getByText('Posted')).toBeVisible()

    // ---- The close is refused while an account is unreconciled -------------------------
    await page.getByRole('radio', { name: 'Balancing' }).click()
    const blocked = page.getByRole('status').filter({ hasText: /cannot be closed yet/i })
    await expect(blocked).toBeVisible()
    // The entry just posted moved the books, so the account is no longer reconciled against them.
    await expect(blocked).toContainText(/Not balanced yet/i)
    // The button is not offered at all while it would fail.
    await expect(page.getByRole('button', { name: 'Close month' })).toHaveCount(0)

    // ---- Reconcile every account to what the books say --------------------------------
    await reconcileEveryAccount(page)

    // ---- A pending entry is not money yet, so the month cannot close over it either ----
    // The seeded pending entry was added by the finance assistant, so the CFO may sign it off: nobody approves
    // their own. Clearing the queue is part of every real month end.
    await page.getByRole('radio', { name: 'Ledger' }).click()
    await page.getByRole('button', { name: 'Waiting' }).click()
    let waiting = page.getByRole('table').getByRole('row').filter({ hasText: 'Pending' })
    while ((await waiting.count()) > 0) {
      await waiting.first().getByRole('button', { name: /Actions for/ }).click()
      await page.getByRole('menuitem', { name: 'Approve' }).click()
      await expect(page.getByText(/Entry approved and posted/i).first()).toBeVisible()
      waiting = page.getByRole('table').getByRole('row').filter({ hasText: 'Pending' })
    }

    // Approving moved the books again, so reconcile once more — the reconciliation has to match what it signs off.
    await page.getByRole('radio', { name: 'Balancing' }).click()
    await reconcileEveryAccount(page)

    // ---- Now it closes ----------------------------------------------------------------
    await page.getByRole('button', { name: 'Close month' }).click()
    const closeDialog = page.getByRole('alertdialog')
    await expect(closeDialog.getByText(/Nothing is pending and every account is balanced/i)).toBeVisible()
    await closeDialog.getByRole('button', { name: 'Close the month' }).click()

    // The banner and the toast both say so; either is proof enough.
    await expect(page.getByRole('status').filter({ hasText: /is closed/i }).first()).toBeVisible()

    // ---- A closed month refuses new entries dated inside it ---------------------------
    await page.getByRole('radio', { name: 'Ledger' }).click()
    await page.getByRole('button', { name: 'New entry' }).click()
    const late = page.getByRole('dialog')
    await late.getByLabel('Fund').selectOption({ label: 'Organization' })
    await late.getByLabel('Account').selectOption({ index: 1 })
    await late.getByLabel('Category').selectOption({ index: 1 })
    await late.getByLabel('Amount (₱)').fill('50')
    await late.getByLabel('Description').fill(`E2E too late ${uniqueLetters()}`)
    await late.locator('input[type="file"]').setInputFiles({
      name: 'receipt.png',
      mimeType: 'image/png',
      buffer: Buffer.from(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==',
        'base64',
      ),
    })
    await late.getByRole('button', { name: 'Post entry' }).click()

    await expect(page.getByText(/This month is closed\. Reopen it, or post a correcting entry dated today\./i)).toBeVisible()
    await late.getByRole('button', { name: 'Cancel' }).click()

    // ---- The close is in the audit trail, with who did it -----------------------------
    await page.getByRole('radio', { name: 'Audit' }).click()
    const audit = page.getByRole('row').filter({ hasText: 'Month closed' }).first()
    await expect(audit).toBeVisible()
    await expect(audit).toContainText('Alex Reyes')
  })
})

/**
 * Tells the books and the statement they agree, for every account the CFO reconciles. The statement figure is read
 * off the row, which is what a CFO does when everything is already accounted for.
 */
async function reconcileEveryAccount(page: Page) {
  const rows = page.getByRole('table').getByRole('row')
  const count = await rows.count()
  for (let i = 1; i < count; i++) {
    const account = rows.nth(i)
    const reconcile = account.getByRole('button', { name: /^Reconcile/ })
    if ((await reconcile.count()) === 0) continue
    // Columns: account, opening, books say, statement says, difference, state.
    const books = (await account.getByRole('cell').nth(2).innerText()).trim()
    await reconcile.click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Statement balance (₱)').fill(moneyToNumber(books))
    await dialog.getByRole('button', { name: 'Save' }).click()
    await expect(dialog).toBeHidden()
  }
  await expect(page.getByRole('table').getByText('Needs check')).toHaveCount(0)
}

/**
 * The Finance sidebar item, which carries a lock: its accessible name is "Finance Board only", so the shared
 * exact-match nav helper cannot find it. Checking the lock is announced is worth a line of its own — it is the
 * design rule that marks the module as board-only.
 */
async function openFinance(page: Page) {
  const menu = page.getByRole('button', { name: 'Open menu' })
  if (await menu.isVisible()) await menu.click()
  const nav = page.getByRole('navigation', { name: 'Main navigation' })
  const link = nav.getByRole('link', { name: /^Finance/ })
  await expect(link).toContainText('Finance')
  await expect(nav.getByLabel('Board only')).toBeVisible()
  await link.click()
}

/** "₱43,000.50" or "−₱250" to the plain number the amount field accepts. */
function moneyToNumber(text: string): string {
  return text.replace(/[₱,\s]/g, '').replace(/−/g, '-')
}
