import { expect, test, type Page } from '@playwright/test'

// Phase 3 screens on the dev seed data: anita (Employee), netagent (Agent on Network),
// ithead (Team Lead on IT) and sysadmin (System Admin). The worker must be running for SLA timers.
const password = process.env.E2E_ADMIN_PASSWORD
const shots = 'test-results/screens'

async function signInAs(page: Page, user: string) {
  await page.goto('/')
  await page.getByLabel('Username or email').fill(user)
  await page.getByLabel('Password').fill(password!)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('heading', { level: 1, name: /Welcome/ })).toBeVisible()
}

async function signOut(page: Page) {
  await page.getByRole('button', { name: 'Sign out' }).click()
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
}

const nav = (page: Page) => page.getByRole('navigation', { name: 'Main' })

test.beforeAll(() => {
  if (!password) throw new Error('Set E2E_ADMIN_PASSWORD to run the end-to-end tests.')
})

test('SLA timers for support, hidden from requesters; availability switch', async ({ page }) => {
  test.setTimeout(90_000)
  const subject = `Switch port dead ${Date.now().toString().slice(-6)}`

  await signInAs(page, 'anita')
  await nav(page).getByRole('link', { name: 'New request' }).click()
  await page.getByLabel('What is it about?').click()
  await page.getByRole('option', { name: 'Network or VPN' }).click()
  await page.getByLabel('Subject').fill(subject)
  await page.getByLabel('Describe the problem or request').fill('The port by desk 14 has no link light.')
  await page.getByRole('button', { name: 'Submit request' }).click()
  await expect(page.getByRole('heading', { level: 1, name: subject })).toBeVisible()
  const ticketUrl = page.url()
  // Requesters never see internal service levels or the agent status switch.
  await expect(page.getByRole('region', { name: 'Service levels' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /Your status/ })).toHaveCount(0)
  await signOut(page)

  await signInAs(page, 'netagent')
  // The worker starts the SLA clocks from the event stream within a few seconds.
  await expect(async () => {
    await page.goto(ticketUrl)
    await expect(page.getByRole('region', { name: 'Service levels' })).toBeVisible({ timeout: 2_000 })
  }).toPass({ timeout: 30_000 })
  const sla = page.getByRole('region', { name: 'Service levels' })
  await expect(sla.getByText('First response', { exact: true })).toBeVisible()
  await expect(sla.getByText('Resolution', { exact: true })).toBeVisible()
  await page.screenshot({ path: `${shots}/p3-ticket-sla.png`, fullPage: true })

  // Replying meets the first-response target.
  await page.getByLabel('Reply to the requester').fill('Checking the switch now.')
  await page.getByRole('button', { name: 'Send reply' }).click()
  // Wait for the reply to be saved before reloading, or the reload would cancel the request.
  await expect(page.getByRole('region', { name: 'Conversation' }).getByText('Checking the switch now.')).toBeVisible()
  await expect(async () => {
    await page.reload()
    await expect(page.getByRole('region', { name: 'Service levels' }).getByText('Met', { exact: true })).toBeVisible({ timeout: 2_000 })
  }).toPass({ timeout: 30_000 })

  // Queue shows the SLA column.
  await nav(page).getByRole('link', { name: 'Tickets' }).click()
  await page.getByRole('tab', { name: 'My teams' }).click()
  await expect(page.getByRole('columnheader', { name: 'SLA' })).toBeVisible()

  // Status switch: go Busy, then back to Available so automatic assignment keeps working for this agent.
  const status = page.getByRole('button', { name: /Your status/ })
  await status.click()
  await page.getByRole('menuitem', { name: /Busy/ }).click()
  await expect(page.getByRole('button', { name: /Your status: Busy/ })).toBeVisible()
  await page.getByRole('button', { name: /Your status/ }).click()
  await page.getByRole('menuitem', { name: /Available/ }).click()
  await expect(page.getByRole('button', { name: /Your status: Available/ })).toBeVisible()
  await signOut(page)

  await signInAs(page, 'ithead')
  await nav(page).getByRole('link', { name: 'Tickets' }).click()
  await expect(page.getByRole('tab', { name: 'Escalated' })).toBeVisible()
})

test('admin screens for SLA policies, calendars and hours', async ({ page }) => {
  test.setTimeout(90_000)
  await signInAs(page, 'sysadmin')

  await nav(page).getByRole('link', { name: 'SLA policies' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'SLA policies' })).toBeVisible()
  await expect(page.getByText('Default - Urgent')).toBeVisible()
  await page.screenshot({ path: `${shots}/p3-sla-policies.png`, fullPage: true })
  // A second active policy for the same scope is refused with a clear message.
  await page.getByRole('button', { name: 'New policy' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Name').fill('Duplicate urgent')
  await dialog.getByLabel('Priority').click()
  await page.getByRole('option', { name: 'Urgent' }).click()
  await expect(dialog.getByRole('group', { name: 'Escalation levels' })).toBeVisible()
  await dialog.getByRole('button', { name: 'Add level' }).click()
  await page.screenshot({ path: `${shots}/p3-sla-policy-dialog.png`, fullPage: true })
  await dialog.getByRole('button', { name: 'Save' }).click()
  await expect(dialog.getByText(/already covers this desk, type and priority/)).toBeVisible()
  await dialog.getByRole('button', { name: 'Cancel' }).click()

  await nav(page).getByRole('link', { name: 'Calendars & holidays' }).click()
  await expect(page.getByRole('heading', { level: 2, name: 'Standard business hours' })).toBeVisible()
  await expect(page.getByText('Mon–Fri 09:00–18:00')).toBeVisible()
  await expect(page.getByText('India national holidays', { exact: true })).toBeVisible()
  await page.screenshot({ path: `${shots}/p3-calendars.png`, fullPage: true })
  await page.getByRole('button', { name: 'New holiday list' }).click()
  await page.getByRole('dialog').getByLabel('List name').fill('Duplicate check')
  for (let i = 0; i < 2; i++) {
    await page.getByRole('button', { name: 'Add holiday' }).click()
    await page.getByLabel('Date').nth(i).fill('2027-12-25')
    await page.getByLabel('Holiday name').nth(i).fill('Christmas')
  }
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText('2027-12-25 is listed twice.')).toBeVisible()
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click()

  await nav(page).getByRole('link', { name: 'Hours & assignment' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Hours & assignment' })).toBeVisible()
  const network = page.getByRole('row').filter({ hasText: 'Network' }).first()
  await expect(network).toContainText('Standard business hours')
  await network.getByRole('button', { name: 'Edit' }).click()
  await expect(page.getByRole('dialog').getByLabel('Assignment')).toBeVisible()
  await page.screenshot({ path: `${shots}/p3-unit-settings.png`, fullPage: true })
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click()
})
