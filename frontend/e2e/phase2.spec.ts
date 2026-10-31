import { expect, test, type Page } from '@playwright/test'

// Phase 2 features on the dev seed data: anita (Employee), netagent (Agent on Network),
// ithead (Team Lead on IT), and a "VPN reset steps" saved reply shared with the Network team.
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

const pdf = (name: string) => ({ name, mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.7\nE2E test document\n') })
const log = (name: string) => ({ name, mimeType: 'text/plain', buffer: Buffer.from('2026-10-31 VPN tunnel dropped: idle timeout\n') })

test.beforeAll(() => {
  if (!password) throw new Error('Set E2E_ADMIN_PASSWORD to run the end-to-end tests.')
})

test('attachments, saved replies, notifications and lead actions', async ({ page }) => {
  test.setTimeout(90_000)
  const stamp = Date.now().toString().slice(-6)
  const subject = `VPN drops with attachment ${stamp}`
  const publicFile = `vpn-report-${stamp}.pdf`
  const internalFile = `tunnel-${stamp}.log`

  // 1. Employee raises a request with a PDF.
  await signInAs(page, 'anita')
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'New request' }).click()
  await page.getByLabel('What is it about?').click()
  await page.getByRole('option', { name: 'Network or VPN' }).click()
  await page.getByLabel('Subject').fill(subject)
  await page.getByLabel('Describe the problem or request').fill('VPN drops every 10 minutes. Report attached.')
  await page.locator('input[type=file]').setInputFiles([pdf(publicFile)])
  await expect(page.getByText(publicFile)).toBeVisible()
  await page.getByRole('button', { name: 'Submit request' }).click()
  await expect(page.getByRole('heading', { level: 1, name: subject })).toBeVisible()
  await expect(page.getByRole('button', { name: `Download ${publicFile}` })).toBeVisible()
  const ticketUrl = page.url()
  await signOut(page)

  // 2. Agent adds an internal note with a log file, then a public reply from a saved reply.
  await signInAs(page, 'netagent')
  await page.goto(ticketUrl)
  await page.getByRole('button', { name: 'Internal note' }).click()
  await page.getByLabel('Internal note').fill('Tunnel log attached for the network team.')
  await page.locator('input[type=file]').setInputFiles([log(internalFile)])
  await page.getByRole('button', { name: 'Add note' }).click()
  const conversation = page.getByRole('region', { name: 'Conversation' })
  await expect(conversation.getByRole('button', { name: `Download ${internalFile}` })).toBeVisible()

  await page.getByRole('button', { name: 'Reply to requester' }).click()
  await page.getByRole('button', { name: 'Insert saved reply' }).click()
  await page.getByRole('menuitem', { name: /VPN reset steps/ }).click()
  await expect(page.getByLabel('Reply to the requester')).toHaveValue(/^Hi Anita, please reset your VPN profile\./)
  await page.getByRole('button', { name: 'Send reply' }).click()
  await expect(conversation.getByText('Hi Anita, please reset your VPN profile.')).toBeVisible()
  await page.screenshot({ path: `${shots}/20-agent-attachments.png`, fullPage: true, animations: 'disabled' })
  await signOut(page)

  // 3. Employee: notification bell leads to the ticket; she sees the PDF, never the internal log.
  await signInAs(page, 'anita')
  await page.getByRole('button', { name: /Notifications, [1-9]\d* unread/ }).click()
  await page.getByRole('menuitem', { name: new RegExp(`replied to your request`) }).first().click()
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible()
  await page.goto(ticketUrl)
  await expect(page.getByText('Hi Anita, please reset your VPN profile.')).toBeVisible()
  await expect(page.getByText(internalFile)).toHaveCount(0)
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: `Download ${publicFile}` }).click()
  expect((await download).suggestedFilename()).toBe(publicFile)
  await page.screenshot({ path: `${shots}/21-requester-attachments.png`, fullPage: true, animations: 'disabled' })
  await signOut(page)

  // 4. Team lead: transfer within IT, delete with a reason, restore from the banner.
  await signInAs(page, 'ithead')
  await page.goto(ticketUrl)
  await page.getByRole('button', { name: 'Transfer' }).click()
  await page.getByLabel('New team').click()
  await page.getByRole('option', { name: /Service Desk/ }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Transfer' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()
  await page.getByRole('tab', { name: 'History' }).click()
  await expect(page.getByText(/transferred it from Network to Service Desk/)).toBeVisible()
  await page.getByRole('tab', { name: 'Details' }).click()

  await page.getByRole('button', { name: 'Delete' }).click()
  await page.getByLabel('Reason').fill(`E2E duplicate ${stamp}`)
  await page.getByRole('dialog').getByRole('button', { name: 'Delete' }).click()
  await expect(page.getByText(/This ticket was deleted on/)).toBeVisible()
  await page.screenshot({ path: `${shots}/22-deleted-ticket.png`, fullPage: true, animations: 'disabled' })
  await page.getByRole('button', { name: 'Restore' }).click()
  await expect(page.getByText(/This ticket was deleted on/)).toHaveCount(0)
})
