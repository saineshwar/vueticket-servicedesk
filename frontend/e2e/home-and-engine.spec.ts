import { expect, test, type Page } from '@playwright/test'

// Home dashboard per role, the account panel in the top bar, and the admin Engine page.
// Needs the API, the worker and Vite running, on the dev seed data.
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

test.beforeAll(() => {
  if (!password) throw new Error('Set E2E_ADMIN_PASSWORD to run the end-to-end tests.')
})

test('home dashboard shows each person only what applies to them', async ({ page }) => {
  await signInAs(page, 'anita')
  await expect(page.getByRole('region', { name: 'My requests' })).toBeVisible()
  await expect(page.getByRole('region', { name: 'My work' })).toHaveCount(0)
  await expect(page.getByRole('region', { name: 'Team overview' })).toHaveCount(0)
  await signOut(page)

  await signInAs(page, 'netagent')
  await expect(page.getByRole('region', { name: 'My work' })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Team overview' })).toHaveCount(0)
  await signOut(page)

  await signInAs(page, 'ithead')
  const team = page.getByRole('region', { name: 'Team overview' })
  await expect(team).toBeVisible()
  await expect(team.getByRole('table', { name: 'Agent load' })).toBeVisible()
  // The chart has a table view with the same numbers.
  await team.getByRole('button', { name: 'Show as table' }).click()
  await expect(team.getByRole('table', { name: 'Created and resolved, last 14 days' }).getByRole('row')).toHaveCount(15)
  await page.screenshot({ path: `${shots}/h-lead-dashboard.png`, fullPage: true })

  // A tile opens the matching queue tab.
  await team.getByRole('link', { name: /Unassigned/ }).click()
  await expect(page.getByRole('tab', { name: 'Unassigned', selected: true })).toBeVisible()

  // "Your access" lives in the account panel next to Sign out.
  await page.getByRole('button', { name: /your account and access/ }).click()
  const account = page.getByRole('dialog', { name: 'Your account' })
  await expect(account.getByText('Your access')).toBeVisible()
  await expect(account.getByText('dashboard.team')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(account).toHaveCount(0)
})

test('admin sees when each engine task ran', async ({ page }) => {
  await signInAs(page, 'sysadmin')
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Engine' }).click()
  const tasks = page.getByRole('table', { name: 'Tasks' })
  const events = tasks.getByRole('row').filter({ hasText: 'Ticket events' }).first()
  await expect(events).toBeVisible()
  await expect(events.getByText('Running')).toBeVisible()
  await expect(page.getByRole('region', { name: 'Run log' })).toBeVisible()
  await page.getByLabel('Failures only').check()
  await page.screenshot({ path: `${shots}/engine.png`, fullPage: true })
  await signOut(page)

  // Support staff without config.jobs cannot open it.
  await signInAs(page, 'netagent')
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Engine' })).toHaveCount(0)
  await page.goto('/admin/engine')
  await expect(page.getByRole('heading', { level: 1, name: /Welcome/ })).toBeVisible()
})
