# The engine worker

`TicketCore.Worker` runs everything time-based: SLA clocks, reminders, escalations, unassigned alerts, automatic
assignment, auto-close and outgoing email. It is a separate process from the API, deployed and scaled on its own.
It uses no SQL Agent jobs, Hangfire or other database schedulers. Written for the developers and operators who run it.

## How it works

```
 API (TicketCore.Api)                          Engine worker (TicketCore.Worker)
 ───────────────────                           ─────────────────────────────────
 writes Ticket + TicketHistory  ──►  SQL  ◄──  reads TicketHistory after its checkpoint
 knows nothing about SLA rules       Server    applies events to TicketSla, TicketEscalation
 queues EmailQueue rows                        sends EmailQueue rows, writes Notification
```

- **The API only records facts.** Each ticket change is written to `TicketHistory` in the same transaction as the change.
  The API holds no SLA logic and keeps working when the worker is down.
- **The worker treats `TicketHistory` as an event stream.** It reads events after the checkpoint in
  `EngineCheckpoint('ticket-history')` and applies each one *at the time it happened*. After downtime it catches up
  with correct results: a reply made while the worker was stopped still counts as on time.
- **Business rules are pure C#** (`TicketCore.Engine`: `BusinessCalendar`, `SlaRules`, `AssignmentRules`).
  They take UTC times in and give UTC times out, and have no database access. They are covered by unit tests in `TicketCore.Engine.Tests`.

### Tasks

Each task runs on its own interval (section `Engine` in `appsettings.json`):

| Task | Default interval | Does |
|---|---|---|
| `sla-events` | 5 s | Reads new history events; starts, pauses, meets and recalculates SLA clocks; clears escalations |
| `sla-monitor` | 30 s | Sends reminders and missed-target notices when due; starts escalations; starts tracking for open tickets that have none |
| `escalation` | 60 s | Moves escalations up a level when the window ends; reminds the final level |
| `assignment` | 30 s | Assigns new tickets to available agents |
| `unassigned-watch` | 60 s | Alerts leads about tickets nobody has picked up |
| `auto-close` | 300 s | Closes resolved tickets after the desk's auto-close time |
| `log-cleanup` | 3600 s | Deletes run history older than `RunLogRetentionDays` (30) |
| email sender | continuous | Sends `EmailQueue` rows with retries (when `Email:DispatcherEnabled` is true) |

Calendars, policies and team settings are cached and reloaded every `ReferenceDataRefreshSeconds` (120 s).

### Running more than one worker

It is safe to run several instances for availability:

- **One event reader at a time.** The event stream is leased for 30 seconds and renewed every run.
  If that worker crashes, another takes over within 30 seconds. On a normal shutdown the lease is released immediately.
- **Due SLA rows are claimed** with a short lease (`TicketSla.LeaseUntil`), so two workers never process the same ticket at once.
- **Every write is conditional** on what was read: `TicketSla.RowVersion` for SLA state, the current level for escalations,
  and "still unassigned" for assignment. A worker that loses the race writes nothing and sends nothing.
- **Never judged on stale data.** The SLA and escalation monitors skip any ticket with history events the reader has not
  applied yet, and check it again once the reader catches up.

## Deploying

The worker needs the same database connection string as the API, and the SMTP settings if it sends email.
It does not need the JWT key or file storage.

### Windows service

```powershell
cd backend
dotnet publish src/TicketCore.Worker -c Release -o C:\VueTicket\engine

# Configuration: C:\VueTicket\engine\appsettings.Production.json, or environment variables such as
#   ConnectionStrings__Database, Email__SmtpHost, Email__Mode=Smtp
sc.exe create "VueTicket Engine" binPath= "C:\VueTicket\engine\TicketCore.Worker.exe" start= delayed-auto
sc.exe failure "VueTicket Engine" reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc.exe start "VueTicket Engine"
```

Set `DOTNET_ENVIRONMENT=Production` for the service (or rely on the default, which is Production).

### Linux (systemd)

```ini
# /etc/systemd/system/vueticket-engine.service
[Unit]
Description=VueTicket Engine
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/vueticket/engine
ExecStart=/opt/vueticket/engine/TicketCore.Worker
Environment=ConnectionStrings__Database=Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True
Restart=always
RestartSec=10
User=vueticket

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now vueticket-engine
```

### Order of upgrades

1. Run the database migrator (`TicketCore.Database`).
2. Deploy the API.
3. Deploy the worker.

At start-up the worker checks that the database has the scripts it needs. If not, it stops with a message saying
which script is missing, instead of running against an old schema.

### Email: only one sender

Set `Email:DispatcherEnabled` to `true` on the worker and `false` on the API (the defaults), so emails are sent from one
place. In Pickup mode (development) `.eml` files are written under the worker's `App_Data/mail`. Use an absolute
`Email:PickupPath` if you want both processes to write to one folder.

## Monitoring

### In the app: Administration → Engine

People with `config.jobs` (System Admins by default) see:

- **Status**: whether all tasks are running, how many workers are alive, how many ticket events are waiting to be read,
  and which worker holds the event reader.
- **Tasks**: for each task, its health, when it last ran, when it runs next, what the last run did and how long it took,
  total runs and failures, and the last error.
  Health is *Running*, *Late* (missed more than three intervals), *Failing* (last run failed), *Stopped* (that worker has
  been silent for 2 minutes, e.g. after a restart) or *Disabled* (turned off in configuration).
- **Run log**: every run that did something or failed, with its error message, filterable by task and by failures.
  Idle runs only update the task list, to keep the log readable.

The page refreshes itself every 10 seconds. The data comes from two tables the worker writes after each run:
`EngineTaskState` (latest state per worker and task) and `EngineRunLog`. The worker's `log-cleanup` task deletes
entries older than `Engine:RunLogRetentionDays` (30 days) every hour. Writing this history is best effort: if it fails,
the worker logs a warning and carries on with its real work.

### On the server

| Endpoint | Returns |
|---|---|
| `GET /health` | 200 when the process is up (for load balancers and service monitors) |
| `GET /status` | Instance id and, per task: enabled, last run, items processed, last error and time |

The worker listens on `http://localhost:5231` by default (`Urls`). Keep it off the public network.
Logs go to `logs/vueticket-engine-<date>.log` next to the executable (NLog). Each failed run is logged as an error and retried on the next interval.
A failed event stops the reader at that event (it is retried, never skipped).

## Troubleshooting

| Symptom | Check |
|---|---|
| No SLA panel on new tickets | Is the worker running (Administration → Engine, or `/health`)? Is there an active policy for the ticket's priority? |
| A task shows *Failing* | Open the Run log with *Failures only* for the error; the worker log has the full stack trace |
| Timers don't move on weekends or at night | Expected: the team's calendar is closed. Check *Hours & assignment*. |
| Nobody gets tickets automatically | Agents must be *Available* and hold `agent.receive.auto`; the team must not be *Manual*; it must be within working hours; agents must be under the ticket limit |
| Configuration change not applied | Wait up to `ReferenceDataRefreshSeconds`; changes apply to new targets only |
| Escalation went to System Admins | Nobody holds the level's role on the team or any unit above it. Give the role in *Organization*. |
| Event reader not advancing | `SELECT * FROM EngineCheckpoint`: compare `LastEventId` with `MAX(TicketHistoryId)`; check the worker log for the failing event |

## Development settings

For quick manual testing, shorten the intervals with environment variables:

```powershell
$env:Engine__ReferenceDataRefreshSeconds = 5
$env:Engine__SlaMonitor__IntervalSeconds = 10
$env:Engine__Escalation__IntervalSeconds = 10
dotnet run --project src/TicketCore.Worker --launch-profile worker
```

A short inactive policy, *TEST fast urgent (IT Help)* (1-minute targets), exists in the development database for this.
Turn it on in *SLA policies* and put the team on the *24x7* calendar so the clock runs at any hour.
Turn both back afterwards.
