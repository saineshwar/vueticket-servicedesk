import { expect, test, type Page } from '@playwright/test'

// Plays a ticket through three people on the dev seed data:
//   anita (Employee, HR)  ·  netagent (Agent on IT › Network)  ·  sysadmin
// The IT Help desk with a "Network or VPN" category routed to Network must exist.
// All seed users share E2E_ADMIN_PASSWORD in the dev database.
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

test('employee raises a request, agent works it, internal notes stay internal', async ({ page }) => {
  const stamp = Date.now().toString().slice(-6)
  const subject = `Wi-Fi drops in meeting room ${stamp}`
  const secret = `Internal: AP-3 firmware is outdated ${stamp}`

  // 1. Employee: only requester menus, then raises a request.
  await signInAs(page, 'anita')
  const nav = page.getByRole('navigation', { name: 'Main' })
  await expect(nav.getByRole('link', { name: 'New request' })).toBeVisible()
  await expect(nav.getByRole('link', { name: 'Tickets' })).toHaveCount(0)
  await expect(nav.getByRole('link', { name: 'Organization' })).toHaveCount(0)

  await nav.getByRole('link', { name: 'New request' }).click()
  await page.getByLabel('What is it about?').click()
  await page.getByRole('option', { name: 'Network or VPN' }).click()
  await page.getByLabel('Subject').fill(subject)
  await page.getByLabel('Describe the problem or request').fill('The Wi-Fi in room 4B disconnects every few minutes during calls.')
  await page.screenshot({ path: `${shots}/10-new-request.png`, fullPage: true, animations: 'disabled' })
  await page.getByRole('button', { name: 'Submit request' }).click()

  await expect(page.getByText(/Request IT-\d+ created/)).toBeVisible()
  await expect(page.getByRole('heading', { level: 1, name: subject })).toBeVisible()
  const ticketUrl = page.url()
  await signOut(page)

  // 2. Agent on the Network team: sees it in the team queue, adds a note and a public reply.
  await signInAs(page, 'netagent')
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Tickets' }).click()
  await page.getByRole('tab', { name: 'My teams' }).click()
  // Queues sort by SLA due time, so a new ticket may not be on the first page: search for it.
  await page.getByLabel('Search subject or ticket number').fill(subject)
  await page.getByRole('button', { name: 'Search', exact: true }).click()
  await page.getByRole('link', { name: subject }).click()
  await expect(page.getByRole('heading', { level: 1, name: subject })).toBeVisible()

  await page.getByRole('button', { name: 'Internal note' }).click()
  await page.getByLabel('Internal note').fill(secret)
  await page.getByRole('button', { name: 'Add note' }).click()
  // Wait for the note in the conversation (not the text still sitting in the reply box).
  const conversation = page.getByRole('region', { name: 'Conversation' })
  await expect(conversation.getByText(secret)).toBeVisible()

  await page.getByRole('button', { name: 'Reply to requester' }).click()
  await page.getByLabel('Reply to the requester').fill('We are replacing the access point. Can you test again tomorrow morning?')
  await page.getByLabel('Then set status').click()
  await page.getByRole('option', { name: 'Awaiting requester' }).click()
  await page.getByRole('button', { name: 'Send reply' }).click()
  // Wait for the saved reply (the status text is already on screen in the picker), or signing out would cancel the request.
  await expect(conversation.getByText(/We are replacing the access point/)).toBeVisible()
  await expect(page.getByRole('main').getByText('Awaiting requester').first()).toBeVisible()
  await page.screenshot({ path: `${shots}/11-agent-ticket.png`, fullPage: true, animations: 'disabled' })
  await signOut(page)

  // 3. Employee: sees the reply, never the internal note; replying hands it back to support.
  await signInAs(page, 'anita')
  await page.goto(ticketUrl)
  await expect(page.getByText('We are replacing the access point.')).toBeVisible()
  await expect(page.getByText(secret)).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Internal note' })).toHaveCount(0)
  await page.getByLabel('Add a reply').fill('Tested this morning, it is stable now.')
  await page.getByRole('button', { name: 'Send reply' }).click()
  await expect(page.getByText('In progress').first()).toBeVisible()
  await page.screenshot({ path: `${shots}/12-requester-ticket.png`, fullPage: true, animations: 'disabled' })

  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'My requests' }).click()
  await expect(page.getByRole('link', { name: subject })).toBeVisible()
  await page.screenshot({ path: `${shots}/13-my-requests.png`, fullPage: true, animations: 'disabled' })
})

test('employee from another department cannot open the ticket', async ({ page }) => {
  await signInAs(page, 'bob')
  await page.goto('/tickets/1')
  await expect(page.getByText('This ticket does not exist or you do not have access to it.')).toBeVisible()
})

test('admin configures service desks and users', async ({ page }) => {
  await signInAs(page, 'sysadmin')
  await page.getByRole('link', { name: 'Service desks' }).click()
  await expect(page.getByRole('heading', { level: 2, name: 'IT Help' })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'Network or VPN' })).toBeVisible()
  await page.screenshot({ path: `${shots}/14-service-desks.png`, fullPage: true, animations: 'disabled' })

  await page.getByRole('link', { name: 'Users' }).click()
  await expect(page.getByRole('cell', { name: 'anita', exact: true })).toBeVisible()
  await page.screenshot({ path: `${shots}/15-users.png`, fullPage: true, animations: 'disabled' })
})
