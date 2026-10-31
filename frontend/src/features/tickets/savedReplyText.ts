import type { TicketDetail } from '../../api/types'

/** Placeholders a saved reply may use. Unknown ones are left as typed. */
export const placeholders = ['{requester.firstName}', '{requester.name}', '{ticket.id}', '{ticket.subject}', '{agent.name}']

export function fillPlaceholders(body: string, ticket: TicketDetail, agentName: string): string {
  const firstName = ticket.requesterName.split(' ')[0] ?? ticket.requesterName
  return body
    .replaceAll('{requester.firstName}', firstName)
    .replaceAll('{requester.name}', ticket.requesterName)
    .replaceAll('{ticket.id}', ticket.trackingId)
    .replaceAll('{ticket.subject}', ticket.subject)
    .replaceAll('{agent.name}', agentName)
}
