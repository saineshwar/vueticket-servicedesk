# VueTicket (rebuild)

Self-hosted service management for one large organization: customer and employee portals, a shared support
workspace, custom roles on an organization tree, SLA and hierarchy-based escalation.
Plan: see the "VueTicket Rebuild Plan" document (v2, enterprise).

This repository is the new build. The old ASP.NET MVC app (`../VueTicketCore`) and its database (`VueTicketDB`) are not touched.

## Status

**Phase 0: Foundation (first slice done)**

| Area | Done |
|---|---|
| Solution | `TicketCore.*` layers kept from the old app: Common, Models, ViewModels, Data (Queries / Commands), Core, Services, Api, Database |
| Database | DbUp migrations: organization tree with closure table, users, permissions, roles, role assignments, refresh tokens, audit log |
| Auth | Login by username or email, server-side PBKDF2, upgrade of old SHA-256 hashes at login, lockout, JWT access token + rotating refresh cookie with reuse detection, rate limits |
| Access | 36-permission catalog, role templates (System Admin, Agent, Team Lead, Head of Department, Business Unit Head, Desk Admin), scoped checks on every change, no handing out permissions you don't hold |
| API | `/setup`, `/auth`, `/me`, `/me/permissions`, `/org-units`, `/roles`, `/permissions`, `/role-assignments`, `/users` |
| UI | First-run setup, sign in, menus built from permissions, organization tree (add, edit, move, deactivate, give roles), role editor |
| Tests | 23 Core unit tests, 9 API security tests, 3 Playwright end-to-end tests |

**Phase 1/2: Service desks and tickets (first slice done)**

| Area | Done |
|---|---|
| Configuration | Service desks (code prefixes ticket numbers, audience: employees / customers / both), categories that route to a team, priorities, ticket types |
| Tickets | Raise for yourself or on behalf of someone, tracking IDs like `IT-1001`, My requests, queues (Assigned to me, My teams, Unassigned) |
| Ticket detail | Public replies and internal notes, status, priority, assignment, history; replies move the status automatically |
| Access | Requester, assignee, or `ticket.view` on the ticket's team; everyone else gets 404. Internal notes are never sent to requesters |
| Admin UI | Users (list, create), Service desks (desks and categories) |
| Tests | 43 Core + 14 API tests, 6 Playwright tests including a three-person ticket flow |

**Phase 2: second slice done**

| Area | Done |
|---|---|
| Attachments | On create, on replies and notes, or added later. Stored on disk (`Storage:LocalPath`) behind `IFileStorage`. Limits: 10 MB, 5 per upload, allow-listed types, and the file's first bytes must match its extension. Files on internal notes are never served to requesters. Downloads are access-checked and always "save as" with `nosniff` |
| Ticket actions | Transfer to another team (assignee cleared), delete with a reason, restore, and a Deleted queue for people with `ticket.restore` |
| Saved replies | Personal or shared with a team (shared ones managed with `kb.edit`), with placeholders such as `{requester.firstName}` |
| Notifications | In-app bell (refreshes every 30 s) and email for: request received, support replied, requester replied, assigned to you, resolved |
| Email | Queued in `EmailQueue`, sent by a background sender with retries. `Email:Mode=Pickup` writes `.eml` files to `App_Data/mail` for development; `Smtp` sends through your server (put the password in user-secrets) |
| Hardening | Login bookkeeping is atomic (parallel sign-ins can't crash it or skip the lockout counter). Requests the browser cancels are not logged as errors. `nosniff`, `X-Frame-Options` and `Referrer-Policy` headers on every response |

**Phase 3: SLA, escalation and automation (done)**

| Area | Done |
|---|---|
| Engine | Separate, independently deployable worker (`TicketCore.Worker`, Windows service or systemd). No database jobs: it reads `TicketHistory` as an event stream, so it catches up correctly after downtime and several instances can run side by side. See [docs/engine-worker.md](docs/engine-worker.md) |
| Calendars | Business hours per calendar with IANA time zones (DST-safe), holiday lists, a default calendar, and calendars set on any unit and inherited down the tree |
| SLA | First response, next response and resolution targets in business time; pause while awaiting the requester; reminders; missed-target notices; recalculation on priority change. Most specific policy wins (desk + type → desk → type → all) |
| Escalation | Up to 4 levels by role (default Team Lead → Head of Department → Business Unit Head), owned by the nearest role holder above the team, System Admins as a fallback. Final level is reminded every 4 business hours. Cleared by a reply or by resolving the ticket |
| Assignment | Agent status (Available / Busy / Away / Offline); load-balanced, round-robin or manual per team; a per-agent ticket limit; only within working hours; unassigned alerts to leads |
| Housekeeping | Auto-close resolved tickets after a time set per desk (default 48 h, 0 = never) |
| UI | *Service levels* panel and escalation banner on tickets, SLA column and *Escalated* queue, status switch in the top bar, admin screens for SLA policies, calendars and holidays, and hours and assignment per unit |
| Tests | 40 engine, 56 Core and 14 API tests; 9 Playwright tests including SLA timers, the status switch and the admin screens |

How SLAs and escalation behave, for admins and team leads: [docs/sla-and-escalation.md](docs/sla-and-escalation.md).

**Home dashboard and monitoring**

| Area | Done |
|---|---|
| Home dashboard | One page for everyone, showing only what applies: own requests (open, waiting for your reply, recently resolved); for support staff, their work (assigned, overdue, due within an hour, unassigned and escalated queues, next tickets by SLA); for people with `dashboard.team`, team figures (open, unassigned, overdue, escalated, open by priority, agent load with status, created vs resolved over 14 days). Tiles open the matching queue tab |
| Account panel | Your name in the top bar opens your email, user type and access (permissions grouped by where they apply) |
| Engine page | Administration → Engine (`config.jobs`): health, last and next run of every worker task, event backlog, and a 30-day log of runs that did work or failed. See [docs/engine-worker.md](docs/engine-worker.md#monitoring) |
| Look and feel | White layout, consistent grids (header row, hover, pagination), tinted status chips, person avatars, queues sorted by SLA urgency |
| Tests | 11 Playwright tests |

**Still open**

- Phase 2 leftovers: reading incoming email into tickets (IMAP / Microsoft 365; needs a mailbox to connect), merge and link, CC/followers, real-time push (SignalR) instead of polling.
- Transactional outbox: the API sends notifications right after committing a ticket change, so a crash at that exact moment can lose one notification (the ticket change itself is safe).
- A seed script for CI. The E2E tests use the dev seed data (Acme Group tree, IT Help desk, users `anita`, `bob`, `netagent`, `ithead`, `sysadmin`).

## Run locally

Prerequisites: .NET 10 SDK, Node 22+, SQL Server.

```powershell
# 1. Secrets (stored in your user profile, never in the repo)
cd backend
dotnet user-secrets set "ConnectionStrings:Database" "Server=YOUR-SERVER;Database=VueTicketV2_Dev;User Id=...;Password=...;TrustServerCertificate=True;" --project src/TicketCore.Api
dotnet user-secrets set "ConnectionStrings:Database" "<same value>" --project src/TicketCore.Database
dotnet user-secrets set "Jwt:SigningKey" "<at least 32 random characters>" --project src/TicketCore.Api

# 2. Create / upgrade the database
dotnet run --project src/TicketCore.Database

# 3. API on http://localhost:5221 (API reference at /scalar in Development)
dotnet run --project src/TicketCore.Api --launch-profile http

# 4. Engine worker on http://localhost:5231 (/health, /status), in a second terminal.
#    It needs the same connection string.
dotnet user-secrets set "ConnectionStrings:Database" "<same value>" --project src/TicketCore.Worker
dotnet run --project src/TicketCore.Worker --launch-profile worker

# 5. Web app on http://localhost:5173 (proxies /api to the API)
cd ../frontend
npm install
npm run dev
```

The first visit shows the setup screen, which creates the organization and the first System Admin.
Without the worker the app still works, but there are no SLA timers, escalations, automatic assignment or emails.
Deploying the worker as a service: [docs/engine-worker.md](docs/engine-worker.md).

## Tests

```powershell
cd backend;  dotnet test
cd frontend; $env:E2E_ADMIN_PASSWORD = "<admin password>"; npx playwright test   # needs API, worker and Vite running
```

## Conventions

- **Data**: `TicketCore.Data/<Module>/Queries` (reads, mostly Dapper) and `Commands` (writes, EF Core or Dapper in a transaction).
- **Rules live in Core**: services check permissions on the exact unit a request touches; controllers only check "has this permission somewhere".
- **Errors**: services return `OperationResult` with a stable `code`; the API returns RFC 7807 problem details with that code and a message written for end users.
- **Schema changes**: add a new numbered script in `TicketCore.Database/Scripts`; never edit one that has shipped. If you add a permission, add it to both `Permissions.cs` and a new seed script (a test checks they match).
- **Time**: UTC in the database, `TimeProvider` injected for testable time.
- **Time-based work belongs in the engine**: the API writes facts (`TicketHistory`), the worker reacts. Keep rules pure (`SlaRules`, `AssignmentRules`, `BusinessCalendar`) and make every engine write conditional, so several workers stay safe.
- **Frontend pages are lazy-loaded** in `app/App.tsx`; add new pages the same way.
