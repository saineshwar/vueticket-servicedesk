using Microsoft.Extensions.Logging;
using TicketCore.Common;
using TicketCore.Core.Notifications;
using TicketCore.Data.Engine;
using TicketCore.Data.Tickets.Queries;

namespace TicketCore.Engine.Sla;

/// <summary>
/// Fires SLA reminders and breaches when they fall due, and starts escalations on breaches the policy escalates.
/// Also starts SLA tracking for open tickets that have none (tickets created before the engine was deployed).
/// Due rows are claimed with a short lease, and every write is conditional, so several workers are safe.
/// </summary>
public sealed class SlaMonitor(
    IEngineStore store,
    ReferenceData reference,
    EscalationOwners owners,
    ITicketQueries tickets,
    ITicketNotifier notifier,
    TimeProvider clock,
    ILogger<SlaMonitor> logger) : IEngineTask
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var checkpoint = await store.GetCheckpointAsync(SlaEventProcessor.StreamName, ct);
        var done = await BackfillAsync(checkpoint, ct);

        var claimed = await store.ClaimDueSlaAsync(now, 100, TimeSpan.FromMinutes(2), ct);
        if (claimed.Count == 0) return done;
        var facts = (await store.GetTicketFactsAsync(claimed, ct)).ToDictionary(f => f.TicketId);

        // Never judge a ticket whose latest changes haven't been read yet: a reply sitting in the backlog would
        // otherwise be reported as a breach. Those tickets are checked again once the stream catches up.
        var pending = await store.FindTicketsWithPendingEventsAsync(claimed, checkpoint, ct);

        foreach (var ticketId in claimed)
        {
            if (pending.Contains(ticketId))
            {
                await store.ReleaseSlaLeaseAsync(ticketId, ct);
                continue;
            }
            try
            {
                if (await ProcessAsync(ticketId, facts.GetValueOrDefault(ticketId), now, ct)) done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "SLA check failed for ticket {TicketId}; it will be retried when its lease expires", ticketId);
            }
        }
        return done;
    }

    private async Task<bool> ProcessAsync(long ticketId, TicketFacts? ticket, DateTime now, CancellationToken ct)
    {
        var row = await store.GetSlaAsync(ticketId, ct);
        if (row is null) return false;
        var state = row.ToState();

        if (ticket is null || ticket.IsDeleted || ticket.Status is TicketStatus.Closed or TicketStatus.Rejected)
        {
            state.NextEventAt = null;
            await store.UpdateSlaAsync(state.ToRow(), ct);
            return false;
        }

        var events = SlaRules.Evaluate(state, now);
        // Record first, notify after: if another worker or the API changed the row meanwhile, nothing is sent and the
        // lease expires so the row is checked again.
        if (!await store.UpdateSlaAsync(state.ToRow(), ct)) return false;
        if (events.Count == 0) return false;

        var policy = await reference.PolicyByIdAsync(state.SlaPolicyId, ct);
        var record = await tickets.GetAsync(ticketId, includeDeleted: false, ct);
        if (policy is null || record is null) return true;
        var firstLevelRole = policy.LevelAt(1)?.RoleId;

        foreach (var ev in events)
        {
            var what = ClockName(ev.Clock);
            if (ev.Kind == SlaEventKind.Reminder)
            {
                var to = await AssigneeOrLeadsAsync(ticket, firstLevelRole, ct);
                await notifier.NotifyStaffAsync(to, record, "sla_reminder",
                    $"[{record.TrackingId}] {what} due soon",
                    $"{what} on \"{record.Subject}\" ({record.PriorityName} priority) is due soon.", ct);
                continue;
            }

            await store.AddHistoryAsync(ticketId, EngineActivities.SlaBreached, null, ev.Clock, now, ct);
            var breachTo = (await AssigneeOrLeadsAsync(ticket, firstLevelRole, ct))
                .Concat(firstLevelRole is { } r ? await owners.FindAsync(ticket.TeamId, r, ct) : []).Distinct().ToList();
            await notifier.NotifyStaffAsync(breachTo, record, "sla_breached",
                $"[{record.TrackingId}] {what} overdue",
                $"{what} on \"{record.Subject}\" ({record.PriorityName} priority) is now overdue.", ct);

            await EscalateAsync(ticket, record, policy, ev.Clock, now, ct);
        }
        return true;
    }

    private async Task EscalateAsync(TicketFacts ticket, TicketRecord record, SlaPolicyDefinition policy, string clock, DateTime now, CancellationToken ct)
    {
        var calendar = await reference.CalendarAsync(null, ticket.TeamId, ct);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var activeRow = await store.GetActiveEscalationAsync(ticket.TicketId, ct);
            var active = activeRow?.ToState();
            var (result, step) = SlaRules.OnBreach(active, clock, ticket.TicketId, now, policy, calendar);

            switch (step)
            {
                case SlaRules.EscalationStep.None:
                    return;

                case SlaRules.EscalationStep.ReasonAdded:
                    if (await store.UpdateEscalationAsync(result!.ToRow(), activeRow!.Level, ct)) return;
                    continue;

                case SlaRules.EscalationStep.Started:
                    var level = policy.LevelAt(1)!;
                    var people = await owners.FindAsync(ticket.TeamId, level.RoleId, ct);
                    result!.OwnerUserId = people.FirstOrDefault() is var first && first != 0 ? first : null;
                    if (!await store.InsertEscalationAsync(result.ToRow(), ct)) continue; // someone else started one; add the reason to it
                    await store.AddHistoryAsync(ticket.TicketId, EngineActivities.Escalated, null, $"1|{level.RoleName}", now, ct);
                    await notifier.NotifyStaffAsync(people, record, "escalated",
                        $"[{record.TrackingId}] Escalated to you: {record.Subject}",
                        $"{record.TrackingId} breached its {ClockName(clock).ToLowerInvariant()} target and is escalated to you as {level.RoleName} (level 1).", ct);
                    return;
            }
        }
    }

    private async Task<IReadOnlyList<long>> AssigneeOrLeadsAsync(TicketFacts ticket, int? leadRoleId, CancellationToken ct) =>
        ticket.AssigneeUserId is { } a ? [a] : leadRoleId is { } r ? await owners.FindAsync(ticket.TeamId, r, ct) : [];

    /// <summary>Open tickets without SLA state (created before the engine ran, or while no policy existed) start tracking here.</summary>
    private async Task<int> BackfillAsync(long checkpoint, CancellationToken ct)
    {
        var missing = await store.FindOpenTicketsWithoutSlaAsync(50, checkpoint, ct);
        if (missing.Count == 0) return 0;
        var started = 0;
        foreach (var t in await store.GetTicketFactsAsync(missing, ct))
        {
            var policy = await reference.PolicyForAsync(t.ServiceDeskId, t.TicketTypeId, t.PriorityId, ct);
            if (policy is null) continue;
            var (calendarId, calendar) = await reference.CalendarForTeamAsync(t.TeamId, ct);
            var state = SlaRules.Start(t.TicketId, DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc), policy, calendarId ?? 0, calendar);
            state.CalendarId = calendarId;
            // Replies made before tracking started still count, so older tickets don't show false first-response breaches.
            if (t.FirstSupportReplyAt is { } replied) SlaRules.SupportReplied(state, DateTime.SpecifyKind(replied, DateTimeKind.Utc));
            if (t.Status == TicketStatus.Resolved) { state.ResolutionMetAt = clock.GetUtcNow().UtcDateTime; state.NextEventAt = SlaRules.NextEventAt(state); }
            if (await store.InsertSlaAsync(state.ToRow(), ct)) started++;
        }
        return started;
    }

    internal static string ClockName(string clock) => clock switch
    {
        SlaClock.FirstResponse => "First response",
        SlaClock.NextResponse => "Next response",
        SlaClock.Resolution => "Resolution",
        _ => clock
    };
}
