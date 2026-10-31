using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketCore.Core.Notifications;
using TicketCore.Data.Engine;
using TicketCore.Data.Tickets.Queries;
using TicketCore.Engine.Sla;

namespace TicketCore.Engine.Housekeeping;

/// <summary>
/// Alerts leads about tickets nobody has picked up: after the team's alert time (in business minutes) the first
/// escalation level's role (Team Lead) is told, after twice that time the second level's (Head of Department).
/// </summary>
public sealed class UnassignedWatch(
    IEngineStore store,
    ReferenceData reference,
    EscalationOwners owners,
    ITicketQueries tickets,
    ITicketNotifier notifier,
    TimeProvider clock,
    ILogger<UnassignedWatch> logger) : IEngineTask
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var alerts = 0;
        foreach (var t in await store.GetUnassignedNewTicketsAsync(500, ct))
        {
            try
            {
                var sla = await store.GetSlaAsync(t.TicketId, ct);
                if (sla is null || (sla.UnassignedAlert1At is not null && sla.UnassignedAlert2At is not null)) continue;

                var settings = await reference.TeamSettingsAsync(t.TeamId, ct);
                var calendar = await reference.CalendarAsync(sla.CalendarId, t.TeamId, ct);
                var waited = calendar.BusinessMinutesBetween(DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc), now);
                var policy = await reference.PolicyByIdAsync(sla.SlaPolicyId, ct);
                if (policy is null) continue;

                int? level;
                if (sla.UnassignedAlert1At is null && waited >= settings.UnassignedAlertMinutes) { sla.UnassignedAlert1At = now; level = 1; }
                else if (sla.UnassignedAlert1At is not null && sla.UnassignedAlert2At is null && waited >= 2 * settings.UnassignedAlertMinutes) { sla.UnassignedAlert2At = now; level = 2; }
                else continue;

                var target = policy.LevelAt(level.Value);
                if (target is null) continue;
                if (!await store.UpdateSlaAsync(sla, ct)) continue; // someone else got here first

                var record = await tickets.GetAsync(t.TicketId, includeDeleted: false, ct);
                if (record is null) continue;
                await notifier.NotifyStaffAsync(await owners.FindAsync(t.TeamId, target.RoleId, ct), record, "unassigned_alert",
                    $"[{record.TrackingId}] Waiting for an agent: {record.Subject}",
                    $"{record.TrackingId} ({record.PriorityName} priority) has waited {waited} working minutes in {record.TeamName} without an agent. Assign it from the Unassigned queue.", ct);
                alerts++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unassigned check failed for ticket {TicketId}", t.TicketId);
            }
        }
        return alerts;
    }
}

/// <summary>Closes resolved tickets the requester hasn't replied to within the desk's auto-close time.</summary>
public sealed class AutoClose(IEngineStore store, TimeProvider clock) : IEngineTask
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var closed = 0;
        foreach (var t in await store.GetResolvedTicketsAsync(200, ct))
        {
            if (now < DateTime.SpecifyKind(t.ResolvedAt, DateTimeKind.Utc).AddHours(t.AutoCloseHours)) continue;
            if (await store.CloseIfStillResolvedAsync(t.TicketId, now, ct)) closed++;
        }
        return closed;
    }
}

/// <summary>Removes run history older than Engine:RunLogRetentionDays, so the monitoring tables stay small.</summary>
public sealed class RunLogCleanup(IEngineMonitorStore monitor, IOptions<EngineOptions> options, TimeProvider clock) : IEngineTask
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var before = clock.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, options.Value.RunLogRetentionDays));
        return await monitor.PruneAsync(before, ct);
    }
}
