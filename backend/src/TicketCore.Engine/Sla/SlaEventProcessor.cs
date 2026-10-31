using Microsoft.Extensions.Logging;
using TicketCore.Common;
using TicketCore.Data.Engine;

namespace TicketCore.Engine.Sla;

/// <summary>
/// Reads the TicketHistory event stream (written by the API) and keeps each ticket's SLA state in step.
/// The API knows nothing about SLA rules. Events are applied at the time they happened, so after downtime the
/// engine catches up with correct due times. Only the worker holding the stream lease processes events.
/// </summary>
public sealed class SlaEventProcessor(
    IEngineStore store,
    ReferenceData reference,
    EngineInstance instance,
    TimeProvider clock,
    ILogger<SlaEventProcessor> logger) : IEngineTask
{
    public const string StreamName = "ticket-history";
    public static readonly TimeSpan LeaseTime = TimeSpan.FromSeconds(30);
    private const int BatchSize = 200;

    private static readonly HashSet<string> Relevant =
    [
        TicketActivities.Created, TicketActivities.Replied, TicketActivities.RequesterReplied, TicketActivities.StatusChanged,
        TicketActivities.PriorityChanged, TicketActivities.Deleted, TicketActivities.Restored
    ];

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // Renewed on every run; if this worker crashes, another takes over within the lease time.
        if (!await store.TryLeaseStreamAsync(StreamName, instance.Id, now, LeaseTime, ct)) return 0;

        var checkpoint = await store.GetCheckpointAsync(StreamName, ct);
        var events = await store.ReadEventsAfterAsync(checkpoint, BatchSize, ct);
        if (events.Count == 0) return 0;

        var facts = (await store.GetTicketFactsAsync(events.Select(e => e.TicketId).Distinct().ToList(), ct)).ToDictionary(f => f.TicketId);
        var processedUpTo = checkpoint;
        var applied = 0;

        foreach (var e in events)
        {
            try
            {
                if (Relevant.Contains(e.Activity) && facts.TryGetValue(e.TicketId, out var ticket) && await ApplyAsync(e, ticket, ct))
                    applied++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Stop here and retry this event next cycle rather than skip it.
                logger.LogError(ex, "Could not apply event {EventId} ({Activity}) on ticket {TicketId}", e.TicketHistoryId, e.Activity, e.TicketId);
                break;
            }
            processedUpTo = e.TicketHistoryId;
        }

        if (processedUpTo > checkpoint && !await store.AdvanceCheckpointAsync(StreamName, instance.Id, checkpoint, processedUpTo, now, ct))
            logger.LogWarning("Lost the event stream lease before saving the checkpoint; another worker will continue");
        return applied;
    }

    private async Task<bool> ApplyAsync(HistoryEvent e, TicketFacts ticket, CancellationToken ct)
    {
        var at = DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc);
        var createdAt = DateTime.SpecifyKind(ticket.CreatedAt, DateTimeKind.Utc);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var row = await store.GetSlaAsync(ticket.TicketId, ct);
            if (row is null)
            {
                var policy = await reference.PolicyForAsync(ticket.ServiceDeskId, ticket.TicketTypeId, ticket.PriorityId, ct);
                if (policy is null) return false; // no SLA applies to this ticket
                var (calendarId, calendar) = await reference.CalendarForTeamAsync(ticket.TeamId, ct);
                var started = SlaRules.Start(ticket.TicketId, createdAt, policy, calendarId ?? 0, calendar);
                started.CalendarId = calendarId;
                if (!await store.InsertSlaAsync(started.ToRow(), ct)) continue; // another worker started it; reload
                if (e.Activity == TicketActivities.Created) return true;
                continue;
            }

            if (e.Activity == TicketActivities.Created) return false;

            var state = row.ToState();
            var current = await reference.PolicyByIdAsync(state.SlaPolicyId, ct)
                          ?? await reference.PolicyForAsync(ticket.ServiceDeskId, ticket.TicketTypeId, ticket.PriorityId, ct);
            if (current is null) return false;
            var cal = await reference.CalendarAsync(state.CalendarId, ticket.TeamId, ct);
            IReadOnlyList<string> met = [];

            switch (e.Activity)
            {
                case TicketActivities.Replied:
                    SlaRules.SupportReplied(state, at);
                    met = SlaRules.ClocksMetBy(supportPublicReply: true, newStatus: null);
                    break;
                case TicketActivities.RequesterReplied:
                    SlaRules.RequesterReplied(state, at, current, cal);
                    break;
                case TicketActivities.StatusChanged:
                    if (Enum.TryParse<TicketStatus>(e.FromValue, out var from) && Enum.TryParse<TicketStatus>(e.ToValue, out var to))
                    {
                        SlaRules.StatusChanged(state, from, to, at, createdAt, current, cal);
                        met = SlaRules.ClocksMetBy(false, to);
                    }
                    break;
                case TicketActivities.PriorityChanged:
                    var newPolicy = await reference.PolicyForAsync(ticket.ServiceDeskId, ticket.TicketTypeId, ticket.PriorityId, ct);
                    if (newPolicy is not null && newPolicy.SlaPolicyId != state.SlaPolicyId) SlaRules.Recalculate(state, createdAt, newPolicy, cal);
                    break;
                case TicketActivities.Deleted:
                    met = SlaRules.AllClocks;
                    break;
                case TicketActivities.Restored:
                    state.NextEventAt = SlaRules.NextEventAt(state);
                    break;
            }

            // Deleted, closed or rejected tickets have nothing left to monitor.
            if (ticket.IsDeleted || ticket.Status is TicketStatus.Closed or TicketStatus.Rejected || e.Activity == TicketActivities.Deleted)
                state.NextEventAt = null;

            if (!await store.UpdateSlaAsync(state.ToRow(), ct)) continue; // changed meanwhile by the monitor; reload and reapply

            if (met.Count > 0) await ClearEscalationAsync(ticket.TicketId, met, at, ct);
            return true;
        }

        logger.LogWarning("Gave up applying {Activity} to ticket {TicketId} after repeated conflicts", e.Activity, ticket.TicketId);
        return false;
    }

    private async Task ClearEscalationAsync(long ticketId, IReadOnlyList<string> met, DateTime at, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var row = await store.GetActiveEscalationAsync(ticketId, ct);
            if (row is null) return;
            var state = row.ToState();
            var before = state.Reasons.Count;
            var cleared = SlaRules.Clear(state, met, at);
            if (!cleared && state.Reasons.Count == before) return; // nothing this event satisfies
            if (await store.UpdateEscalationAsync(state.ToRow(), row.Level, ct))
            {
                if (cleared) await store.AddHistoryAsync(ticketId, EngineActivities.EscalationCleared, row.Level.ToString(), null, at, ct);
                return;
            }
        }
    }
}

/// <summary>History entries the engine writes. It ignores them when it reads the stream back.</summary>
public static class EngineActivities
{
    public const string SlaBreached = TicketActivities.SlaBreached;
    public const string Escalated = TicketActivities.Escalated;
    public const string EscalationCleared = TicketActivities.EscalationCleared;
}
