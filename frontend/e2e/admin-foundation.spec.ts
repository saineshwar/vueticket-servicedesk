import { expect, test, type Page } from '@playwright/test'

// Uses the System Admin created by first-run setup. Pass credentials through the environment:
//   E2E_ADMIN_USER (default "sysadmin") and E2E_ADMIN_PASSWORD (required).
const user = process.env.E2E_ADMIN_USER ?? 'sysadmin'
const password = process.env.E2E_ADMIN_PASSWORD
const shots = 'test-results/screens'

async function signIn(page: Page) {
  await page.goto('/')
  await page.getByLabel('Username or email').fill(user)
  await page.getByLabel('Password').fill(password!)
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('heading', { level: 1, name: /Welcome/ })).toBeVisible()
}

test.beforeAll(() => {
  if (!password) throw new Error('Set E2E_ADMIN_PASSWORD to run the end-to-end tests.')
})

test('wrong password shows the API message', async ({ page }) => {
  await page.goto('/')
  await page.getByLabel('Username or email').fill(user)
  await page.getByLabel('Password').fill('not-the-password-1')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByText('The username or password is incorrect.')).toBeVisible()
  await page.screenshot({ path: `${shots}/01-login-error.png`, animations: 'disabled' })
})

test('admin manages the organization and roles', async ({ page }) => {
  await signIn(page)
  await page.screenshot({ path: `${shots}/02-home.png`, fullPage: true })

  // Organization tree
  await page.getByRole('link', { name: 'Organization' }).click()
  const tree = page.getByRole('tree')
  await expect(tree.getByText('Acme Group')).toBeVisible()
  // The IT department's name and code are both "IT"; the name comes first.
  await tree.getByText('IT', { exact: true }).first().click()
  await expect(page.getByRole('heading', { level: 2, name: 'IT' })).toBeVisible()
  await expect(page.getByText('Priya Rao · IT Admin')).toBeVisible()

  // Add a team under IT
  const stamp = Date.now().toString().slice(-6)
  await page.getByRole('button', { name: 'Add unit' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Name').fill(`E2E Team ${stamp}`)
  await dialog.getByLabel('Code').fill(`E2E-${stamp}`)
  await dialog.getByRole('button', { name: 'Create' }).click()
  await expect(dialog).toBeHidden()
  await expect(page.getByRole('heading', { level: 2, name: `E2E Team ${stamp}` })).toBeVisible()
  await page.screenshot({ path: `${shots}/03-organization.png`, fullPage: true, animations: 'disabled' })

  // A team cannot have children, so "Add unit" is not offered.
  await expect(page.getByRole('button', { name: 'Add unit' })).toHaveCount(0)

  // Units are deactivated, not deleted. Deactivate the test team so runs don't clutter the tree.
  await page.getByRole('button', { name: 'Edit', exact: true }).click()
  const editDialog = page.getByRole('dialog')
  await editDialog.getByLabel('Active').uncheck()
  await editDialog.getByRole('button', { name: 'Save' }).click()
  await expect(editDialog).toBeHidden()
  await expect(page.getByRole('tree').getByText('Inactive')).not.toHaveCount(0)

  // Roles
  await page.getByRole('link', { name: 'Roles' }).click()
  await expect(page.getByText('System Admin', { exact: true })).toBeVisible()
  await expect(page.getByText('Locked', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'New role' }).click()
  await expect(page.getByRole('dialog').getByText('Queues and work')).toBeVisible()
  await page.screenshot({ path: `${shots}/04-role-editor.png`, animations: 'disabled' })
  await page.getByRole('button', { name: 'Cancel' }).click()

  // Reload keeps the session (refresh cookie), sign out ends it.
  await page.reload()
  await expect(page.getByRole('heading', { level: 1, name: 'Roles' })).toBeVisible()
  await page.getByRole('button', { name: 'Sign out' }).click()
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
})

test('organization page works at phone width', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await signIn(page)
  await page.getByRole('button', { name: 'Open menu' }).click()
  await page.getByRole('link', { name: 'Organization' }).click()
  await page.getByRole('tree').getByText('Network').click()
  await expect(page.getByRole('heading', { level: 2, name: 'Network' })).toBeVisible()
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
  expect(overflow).toBe(false)
  await page.screenshot({ path: `${shots}/05-organization-phone.png`, fullPage: true })
})
