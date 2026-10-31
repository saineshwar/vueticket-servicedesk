using TicketCore.Common;
using Microsoft.Extensions.Logging;
using TicketCore.Core.Notifications;
using TicketCore.Data.Engine;
using TicketCore.Data.Tickets.Queries;
using TicketCore.Data.Users.Queries;

namespace TicketCore.Engine.Assignment;

/// <summary>
/// Assigns new, unassigned tickets to available agents in the ticket's team, Urgent and oldest first.
/// Teams set to manual are skipped, and nothing is assigned outside the team's working hours.
/// The assignment itself only applies if the ticket is still unassigned, so it is safe with several workers.
/// </summary>
public sealed class AssignmentEngine(
    IEngineStore store,
    ReferenceData reference,
    IUserQueries users,
    ITicketQueries tickets,
    ITicketNotifier notifier,
    TimeProvider clock,
    ILogger<AssignmentEngine> logger) : IEngineTask
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var waiting = await store.GetUnassignedNewTicketsAsync(200, ct);
        if (waiting.Count == 0) return 0;

        var candidatesByTeam = new Dictionary<int, List<AgentCandidate>>();
        var assigned = 0;

        foreach (var ticket in waiting)
        {
            var settings = await reference.TeamSettingsAsync(ticket.TeamId, ct);
            if (settings.Mode == AssignmentMode.Manual) continue;

            var (_, calendar) = await reference.CalendarForTeamAsync(ticket.TeamId, ct);
            if (!calendar.IsOpen(now)) continue;

            if (!candidatesByTeam.TryGetValue(ticket.TeamId, out var candidates))
            {
                candidates = (await store.GetAvailableAgentsAsync(ticket.TeamId, ct)).ToList();
                candidatesByTeam[ticket.TeamId] = candidates;
            }

            var pick = AssignmentRules.Pick(candidates, settings.Mode, settings.MaxOpenTickets);
            if (pick is null) continue; // nobody available or everyone at the limit: the unassigned watch alerts leads

            var user = await users.GetByIdAsync(pick.UserId, ct);
            if (user is null) continue;
            if (!await store.AssignIfStillUnassignedAsync(ticket.TicketId, pick.UserId, user.DisplayName, now, ct)) continue;

            // Count it in this run, across every team the agent works in.
            foreach (var list in candidatesByTeam.Values)
            {
                var i = list.FindIndex(c => c.UserId == pick.UserId);
                if (i >= 0) list[i] = list[i] with { OpenTickets = list[i].OpenTickets + 1, LastAssignedAt = now };
            }
            assigned++;

            var record = await tickets.GetAsync(ticket.TicketId, includeDeleted: false, ct);
            if (record is not null) await notifier.NotifyAsync(TicketEvent.Assigned, record, actorUserId: null, message: null, ct);
            logger.LogInformation("Assigned ticket {TicketId} to user {UserId}", ticket.TicketId, pick.UserId);
        }
        return assigned;
    }
}
