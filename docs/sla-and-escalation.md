# SLAs, escalation and automatic assignment

How VueTicket decides when a ticket is due, who hears about it when it is late, and who gets new tickets.
Written for administrators who configure it and team leads who work with it.

## The three targets

Every ticket on a desk with a matching SLA policy has up to three targets, counted in **business time**:

| Target | Starts | Met when |
|---|---|---|
| First response | When the ticket is raised | Support sends the first public reply. Internal notes do not count. |
| Next response | When the requester replies after support has already answered | Support replies publicly again |
| Resolution | When the ticket is raised | The ticket is set to Resolved or Closed |

Business time follows the team's calendar. A one-hour target on a ticket raised at 17:30 on a Friday
(hours 09:00–18:00, Monday to Friday) is due at 09:30 on Monday. Holidays on the calendar's holiday lists count as closed.

**Waiting for the requester.** Setting a ticket to *Awaiting requester* pauses the resolution clock and drops any pending
next-response target. When the ticket moves on, the time spent waiting (in business minutes) is added to the resolution due time.

**Changing priority** recalculates the targets from when the ticket was raised, using the new priority's policy.
A ticket made Urgent may therefore be overdue straight away.

**Reopening** a resolved ticket makes the resolution target unmet again. Its due time does not move.

## What people see

Only support staff see SLA information. Requesters never see targets, breaches or escalations.

- **Ticket page.** A *Service levels* panel lists each target as *Due in 2h 10m*, *Overdue by 15m*, *Met*, *Met late*,
  *Missed* or *Paused*, with the exact due time. While a ticket is escalated, a red banner at the top says the level,
  who owns it now, why, and when it moves up next.
- **Queues.** The *SLA* column shows the nearest unmet target. It turns orange within an hour of the due time and red
  once overdue, with an *Escalated L2*-style badge when escalated. Support queues are sorted with escalated tickets first,
  then by the nearest due time.
- **Escalated queue.** Shows open escalations on teams where you hold `queue.escalated`, plus any escalation you own.
- **History.** Records *The first response target was missed*, *Escalated to level 1 (Team Lead)* and
  *The escalation was cleared*, with the time.

## Reminders and missed targets

- **Reminder.** A configured time before a target is due, the assignee is notified (in the app and by email).
  If nobody is assigned, the team leads are notified instead.
- **Missed target.** The assignee (or the team leads) and the holders of the policy's level-1 role are told.
  If the policy escalates on that target, the ticket also escalates.

Each reminder and each missed target is sent once, even if several engine workers are running.

## Escalation

An escalation hands an overdue ticket up the organization:

1. **Level 1** (by default *Team Lead*) owns it for the level's window, for example 30 business minutes for Urgent.
2. If it is still not handled, it moves to **level 2** (*Head of Department*), and so on, up to 4 levels.
3. The **final level** has no window. Its owners get a reminder every 4 business hours until the ticket is handled.

**Who owns each level:** holders of that level's role on the ticket's team. If the team has none, the nearest unit above it
that does is used (department, then business unit, then the organization). If nobody holds the role anywhere above the team,
System Admins are notified, so an escalation is never lost.

**Clearing.** An escalation ends when every target it was raised for is met. A public reply clears a first- or
next-response escalation. Resolving, closing, rejecting or deleting the ticket clears any escalation at once.

The default policies escalate as follows:

| Priority | First response | Next response | Resolution | Reminder | Escalates on |
|---|---|---|---|---|---|
| Urgent | 1h | 2h | 8h | 15m before | first response, resolution |
| High | 2h | 4h | 16h | 30m before | first response, resolution |
| Medium | 4h | 8h | 1d (24h) | 1h before | resolution |
| Low | 8h | 16h | 1d 16h (40h) | 2h before | none |

Default escalation path: Team Lead → Head of Department → Business Unit Head. Level windows are 30, 60, 120 or 240
minutes for Urgent, High, Medium and Low respectively, doubled at level 2.

## Tickets nobody picks up

If a new ticket is still unassigned after the team's **unassigned alert** time (business minutes, default 15),
the team leads are told. After twice that time, the next level up (Head of Department) is told too.

## Automatic assignment

Agents choose their status in the top bar: **Available**, **Busy**, **Away** or **Offline**.
Only *Available* agents who hold `agent.receive.auto` on the team receive tickets automatically,
and only during the team's working hours. Urgent and older tickets are assigned first.

| Mode | Who gets the next ticket |
|---|---|
| Load balanced (default) | The available agent with the fewest open tickets. Ties go to whoever was assigned least recently. |
| Round robin | Available agents in turn |
| Manual | Nobody; leads assign from the *Unassigned* queue |

Agents already at the team's **open tickets per agent** limit (default 20) are skipped. Assignments made by the engine
appear in the history as made by *System*.

## Auto-close

A resolved ticket closes automatically if the requester does not reply within the desk's **auto-close** time
(default 48 hours, wall-clock). Set it to 0 to never close tickets automatically.

## Configuring it

| Screen | Permission | What you set |
|---|---|---|
| Administration → SLA policies | `config.sla` | Targets, reminder and escalation levels per priority. Optionally limit a policy to one desk and/or ticket type. |
| Administration → Calendars & holidays | `config.calendars` | Calendars (time zone, working hours, 24x7), holiday lists, which calendar is the default |
| Administration → Hours & assignment | `config.calendars` / `config.routing` | The calendar for any unit, and each team's assignment mode, ticket limit and unassigned alert |
| Administration → Service desks | `config.desks` | Auto-close time per desk |

Rules worth knowing:

- **Which policy applies.** For a ticket's priority, the most specific active policy wins: desk + type, then desk,
  then type, then the all-desks policy. Only one active policy may cover the same desk, type and priority.
- **Organization-wide settings** (calendars, holiday lists, all-desks policies) need the permission on the organization
  itself. A desk's own policies need `config.sla` on the desk's default team.
- **Which calendar a team uses.** The team's own calendar if set, else the nearest unit above it with one, else the
  default calendar. The *Hours & assignment* screen shows the result and whether it is inherited.
- **Changes apply going forward.** New and changed policies and calendars apply to SLA targets set from then on.
  Tickets already being tracked keep their due times until their priority changes.
  The engine picks up configuration changes within two minutes.
- One calendar must always be the default, and a calendar used by any unit cannot be turned off.
