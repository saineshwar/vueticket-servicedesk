using TicketCore.Common;
using TicketCore.Core.Access;
using TicketCore.Data.Dashboard;
using TicketCore.Data.Tickets.Queries;
using TicketCore.ViewModels.Dashboard;
using TicketCore.ViewModels.Tickets;

namespace TicketCore.Core.Dashboard;

public interface IDashboardService
{
    Task<DashboardResponse> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// One home page for everyone: own requests always; own work for support staff; team figures for people with
/// dashboard.team. Every figure is limited to the teams the caller's permissions cover.
/// </summary>
public sealed class DashboardService(
    IDashboardQueries queries,
    ITicketQueries tickets,
    IPermissionEvaluator permissions,
    ICurrentUser currentUser,
    TimeProvider clock) : IDashboardService
{
    private const int ListSize = 5;

    public async Task<DashboardResponse> GetAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("No signed-in user.");
        var now = clock.GetUtcNow().UtcDateTime;
        var access = await permissions.GetAccessAsync(userId, ct);

        var counts = await queries.GetRequesterCountsAsync(userId, now, ct);
        var recent = await tickets.ListAsync(userId, new TicketListQuery(TicketView.Requested, "all", null, 1, ListSize), ct);
        var requests = new MyRequestsSummary(counts.Open, counts.AwaitingYou, counts.ResolvedLast30Days, recent.Items);

        MyWorkSummary? work = null;
        if (access.HasAnywhere(Permissions.TicketView) || access.HasAnywhere(Permissions.AgentCheckIn))
        {
            var mine = await queries.GetWorkCountsAsync(userId, now, ct);
            var nextUp = await tickets.ListAsync(userId, new TicketListQuery(TicketView.Mine, "open", null, 1, ListSize), ct);
            int? unassigned = access.HasAnywhere(Permissions.QueueUnassigned)
                ? (await tickets.ListAsync(userId, new TicketListQuery(TicketView.Unassigned, "open", null, 1, 1), ct)).Total : null;
            // Escalation owners see their own escalations even without the queue permission.
            var escalated = (await tickets.ListAsync(userId, new TicketListQuery(TicketView.Escalated, "open", null, 1, 1), ct)).Total;
            work = new MyWorkSummary(mine.AssignedOpen, mine.Overdue, mine.DueWithinHour, unassigned,
                access.HasAnywhere(Permissions.QueueEscalated) || escalated > 0 ? escalated : null, nextUp.Items);
        }

        var team = access.HasAnywhere(Permissions.DashboardTeam) ? await queries.GetTeamSummaryAsync(userId, now, ct) : null;
        return new DashboardResponse(requests, work, team);
    }
}
