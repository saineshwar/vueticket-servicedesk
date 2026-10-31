using Microsoft.Extensions.Logging;
using TicketCore.Common;
using TicketCore.Core.Notifications;
using TicketCore.Data.Engine;
using TicketCore.Data.Tickets.Queries;

namespace TicketCore.Engine.Sla;

/// <summary>
/// Moves escalations up the hierarchy when a level's window runs out (Team Lead → Head of Department → Business Unit Head),
/// and reminds the final level every few business hours until the ticket is handled.
/// </summary>
public sealed class EscalationMonitor(
    IEngineStore store,
    ReferenceData reference,
    EscalationOwners owners,
    ITicketQueries tickets,
    ITicketNotifier notifier,
    TimeProvider clock,
    ILogger<EscalationMonitor> logger) : IEngineTask
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // Wall-clock pre-filter; the rules then apply business time exactly.
        var due = await store.FindEscalationsDueAsync(now, now.AddMinutes(-SlaRules.FinalLevelFollowUpMinutes), 100, ct);
        if (due.Count == 0) return 0;

        var facts = (await store.GetTicketFactsAsync(due, ct)).ToDictionary(f => f.TicketId);
        // Don't move an escalation up while a reply or resolution that may clear it is still unread.
        var checkpoint = await store.GetCheckpointAsync(SlaEventProcessor.StreamName, ct);
        var pending = await store.FindTicketsWithPendingEventsAsync(due, checkpoint, ct);
        var done = 0;
        foreach (var ticketId in due.Where(id => !pending.Contains(id)))
        {
            try
            {
                if (await ProcessAsync(ticketId, facts.GetValueOrDefault(ticketId), now, ct)) done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Escalation check failed for ticket {TicketId}", ticketId);
            }
        }
        return done;
    }

    private async Task<bool> ProcessAsync(long ticketId, TicketFacts? ticket, DateTime now, CancellationToken ct)
    {
        var row = await store.GetActiveEscalationAsync(ticketId, ct);
        if (row is null) return false;
        var state = row.ToState();

        // A deleted or finished ticket has nothing left to escalate.
        if (ticket is null || ticket.IsDeleted || ticket.Status is TicketStatus.Resolved or TicketStatus.Closed or TicketStatus.Rejected)
        {
            state.ClearedAt = now;
            await store.UpdateEscalationAsync(state.ToRow(), row.Level, ct);
            return false;
        }

        var sla = await store.GetSlaAsync(ticketId, ct);
        var policy = await reference.PolicyByIdAsync(sla?.SlaPolicyId, ct);
        if (policy is null) return false;
        var calendar = await reference.CalendarAsync(sla?.CalendarId, ticket.TeamId, ct);

        var step = SlaRules.Advance(state, now, policy, calendar);
        if (step == SlaRules.EscalationStep.None) return false;

        var level = policy.LevelAt(state.Level)!;
        var people = await owners.FindAsync(ticket.TeamId, level.RoleId, ct);
        if (step == SlaRules.EscalationStep.Advanced) state.OwnerUserId = people.Count > 0 ? people[0] : null;

        // Conditional on the level we read, so two workers can't both move it up.
        if (!await store.UpdateEscalationAsync(state.ToRow(), row.Level, ct)) return false;

        var record = await tickets.GetAsync(ticketId, includeDeleted: false, ct);
        if (record is null) return true;
        var reasons = string.Join(" and ", state.Reasons.Select(SlaMonitor.ClockName)).ToLowerInvariant();

        if (step == SlaRules.EscalationStep.Advanced)
        {
            await store.AddHistoryAsync(ticketId, EngineActivities.Escalated, row.Level.ToString(), $"{state.Level}|{level.RoleName}", now, ct);
            await notifier.NotifyStaffAsync(people, record, "escalated",
                $"[{record.TrackingId}] Escalated to you: {record.Subject}",
                $"{record.TrackingId} is still overdue ({reasons}) and was not handled at level {row.Level}. It is now escalated to you as {level.RoleName} (level {state.Level}).", ct);
        }
        else
        {
            await notifier.NotifyStaffAsync(people, record, "escalation_followup",
                $"[{record.TrackingId}] Still overdue: {record.Subject}",
                $"{record.TrackingId} is still overdue ({reasons}) at the final escalation level. Please make sure it is handled.", ct);
        }
        return true;
    }
}
