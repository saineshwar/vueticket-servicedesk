using Dapper;
using TicketCore.Common;
using TicketCore.ViewModels.Dashboard;

namespace TicketCore.Data.Dashboard;

public sealed record RequesterCounts(int Open, int AwaitingYou, int ResolvedLast30Days);

public sealed record WorkCounts(int AssignedOpen, int Overdue, int DueWithinHour);

/// <summary>Counts for the home page. Lists reuse ITicketQueries so they look and sort exactly like the queues.</summary>
public interface IDashboardQueries
{
    Task<RequesterCounts> GetRequesterCountsAsync(long userId, DateTime now, CancellationToken ct = default);
    Task<WorkCounts> GetWorkCountsAsync(long userId, DateTime now, CancellationToken ct = default);
    Task<TeamSummary?> GetTeamSummaryAsync(long userId, DateTime now, CancellationToken ct = default);
}

public sealed class DashboardQueries(ISqlConnectionFactory connections) : IDashboardQueries
{
    private const string NameOf = "COALESCE(NULLIF(LTRIM(RTRIM(CONCAT(u.FirstName, ' ', u.LastName))), ''), u.UserName)";

    // Open = New, Open, InProgress, AwaitingRequester, PendingApproval.
    private const string OpenFilter = "t.IsDeleted = 0 AND t.Status IN (1, 2, 3, 4, 7)";

    // The nearest unmet SLA target, ignored while paused (same rule as the queues).
    private const string DueApply = """
        LEFT JOIN dbo.TicketSla s ON s.TicketId = t.TicketId
        OUTER APPLY (SELECT MIN(v) AS DueAt
                     FROM (VALUES (CASE WHEN s.FirstResponseMetAt IS NULL THEN s.FirstResponseDueAt END),
                                  (s.NextResponseDueAt),
                                  (CASE WHEN s.ResolutionMetAt IS NULL THEN s.ResolutionDueAt END)) due(v)
                     WHERE s.PausedAt IS NULL) sd
        """;

    public async Task<RequesterCounts> GetRequesterCountsAsync(long userId, DateTime now, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.QuerySingleAsync<RequesterCounts>(new CommandDefinition($"""
            SELECT
                ISNULL(SUM(CASE WHEN {OpenFilter} THEN 1 ELSE 0 END), 0) AS [Open],
                ISNULL(SUM(CASE WHEN t.IsDeleted = 0 AND t.Status = 4 THEN 1 ELSE 0 END), 0) AS AwaitingYou,
                ISNULL(SUM(CASE WHEN t.IsDeleted = 0 AND t.Status IN (5, 6) AND t.ResolvedAt >= @since THEN 1 ELSE 0 END), 0) AS ResolvedLast30Days
            FROM dbo.Ticket t WHERE t.RequesterUserId = @userId
            """, new { userId, since = now.AddDays(-30) }, cancellationToken: ct));
    }

    public async Task<WorkCounts> GetWorkCountsAsync(long userId, DateTime now, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.QuerySingleAsync<WorkCounts>(new CommandDefinition($"""
            SELECT COUNT(1) AS AssignedOpen,
                   ISNULL(SUM(CASE WHEN sd.DueAt < @now THEN 1 ELSE 0 END), 0) AS Overdue,
                   ISNULL(SUM(CASE WHEN sd.DueAt >= @now AND sd.DueAt < @soon THEN 1 ELSE 0 END), 0) AS DueWithinHour
            FROM dbo.Ticket t
            {DueApply}
            WHERE {OpenFilter} AND t.AssigneeUserId = @userId
            """, new { userId, now, soon = now.AddHours(1) }, cancellationToken: ct));
    }

    public async Task<TeamSummary?> GetTeamSummaryAsync(long userId, DateTime now, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        // Teams where the caller holds dashboard.team, expanded through the closure table.
        const string scope = """
            DECLARE @scope TABLE (OrgUnitId INT PRIMARY KEY);
            INSERT INTO @scope
            SELECT DISTINCT c.DescendantId
            FROM dbo.RoleAssignment ra
            JOIN dbo.RolePermission rp ON rp.RoleId = ra.RoleId AND rp.PermissionKey = @permission
            JOIN dbo.OrgUnitClosure c ON c.AncestorId = ra.OrgUnitId AND (ra.IncludeDescendants = 1 OR c.Depth = 0)
            WHERE ra.UserId = @userId;
            """;
        var today = now.Date;
        using var grid = await db.QueryMultipleAsync(new CommandDefinition($"""
            {scope}
            SELECT COUNT(1) FROM @scope;

            SELECT COUNT(1) AS [Open],
                   ISNULL(SUM(CASE WHEN t.AssigneeUserId IS NULL THEN 1 ELSE 0 END), 0) AS Unassigned,
                   ISNULL(SUM(CASE WHEN sd.DueAt < @now THEN 1 ELSE 0 END), 0) AS Overdue,
                   ISNULL(SUM(CASE WHEN e.TicketEscalationId IS NOT NULL THEN 1 ELSE 0 END), 0) AS Escalated
            FROM dbo.Ticket t
            {DueApply}
            LEFT JOIN dbo.TicketEscalation e ON e.TicketId = t.TicketId AND e.ClearedAt IS NULL
            WHERE {OpenFilter} AND t.TeamId IN (SELECT OrgUnitId FROM @scope);

            SELECT CAST(p.PriorityId AS INT) AS PriorityId, p.Name AS PriorityName, COUNT(t.TicketId) AS [Count]
            FROM dbo.Priority p
            LEFT JOIN dbo.Ticket t ON t.PriorityId = p.PriorityId AND {OpenFilter} AND t.TeamId IN (SELECT OrgUnitId FROM @scope)
            GROUP BY p.PriorityId, p.Name
            ORDER BY p.PriorityId;

            -- Agents who receive work in these teams, plus anyone holding open tickets there.
            WITH agents AS (
                SELECT DISTINCT ra.UserId
                FROM dbo.RoleAssignment ra
                JOIN dbo.RolePermission rp ON rp.RoleId = ra.RoleId AND rp.PermissionKey = @agentPermission
                JOIN dbo.OrgUnitClosure c ON c.AncestorId = ra.OrgUnitId AND (ra.IncludeDescendants = 1 OR c.Depth = 0)
                WHERE c.DescendantId IN (SELECT OrgUnitId FROM @scope)
                UNION
                SELECT t.AssigneeUserId FROM dbo.Ticket t
                WHERE {OpenFilter} AND t.AssigneeUserId IS NOT NULL AND t.TeamId IN (SELECT OrgUnitId FROM @scope)
            )
            SELECT u.UserId, {NameOf} AS Name, CAST(ISNULL(av.Status, 4) AS TINYINT) AS StatusValue,
                   (SELECT COUNT(1) FROM dbo.Ticket t WHERE {OpenFilter} AND t.AssigneeUserId = u.UserId
                        AND t.TeamId IN (SELECT OrgUnitId FROM @scope)) AS [Open],
                   (SELECT COUNT(1) FROM dbo.Ticket t {DueApply}
                    WHERE {OpenFilter} AND t.AssigneeUserId = u.UserId AND t.TeamId IN (SELECT OrgUnitId FROM @scope) AND sd.DueAt < @now) AS Overdue
            FROM agents a
            JOIN dbo.[User] u ON u.UserId = a.UserId AND u.IsActive = 1
            LEFT JOIN dbo.AgentAvailability av ON av.UserId = u.UserId
            ORDER BY [Open] DESC, Name;

            SELECT CAST(t.CreatedAt AS DATE) AS Day, COUNT(1) AS [Count]
            FROM dbo.Ticket t
            WHERE t.IsDeleted = 0 AND t.TeamId IN (SELECT OrgUnitId FROM @scope) AND t.CreatedAt >= @from
            GROUP BY CAST(t.CreatedAt AS DATE);

            SELECT CAST(t.ResolvedAt AS DATE) AS Day, COUNT(1) AS [Count]
            FROM dbo.Ticket t
            WHERE t.IsDeleted = 0 AND t.TeamId IN (SELECT OrgUnitId FROM @scope) AND t.ResolvedAt >= @from
            GROUP BY CAST(t.ResolvedAt AS DATE);
            """,
            new { userId, now, permission = Permissions.DashboardTeam, agentPermission = Permissions.AgentReceiveAuto, from = today.AddDays(-13) },
            cancellationToken: ct));

        if (await grid.ReadSingleAsync<int>() == 0) return null;
        var totals = await grid.ReadSingleAsync<TeamTotals>();
        var priorities = (await grid.ReadAsync<PriorityCount>()).ToList();
        var agents = (await grid.ReadAsync<AgentRow>())
            .Select(a => new AgentLoad(a.UserId, a.Name, ((Users.AgentStatus)a.StatusValue).ToString(), a.Open, a.Overdue)).ToList();
        var created = (await grid.ReadAsync<DayCount>()).ToDictionary(d => d.Day.Date, d => d.Count);
        var resolved = (await grid.ReadAsync<DayCount>()).ToDictionary(d => d.Day.Date, d => d.Count);

        var days = Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13))
            .Select(d => new DailyFlow(d.ToString("yyyy-MM-dd"), created.GetValueOrDefault(d), resolved.GetValueOrDefault(d))).ToList();
        return new TeamSummary(totals.Open, totals.Unassigned, totals.Overdue, totals.Escalated, priorities, agents, days);
    }

    private sealed record TeamTotals(int Open, int Unassigned, int Overdue, int Escalated);
    private sealed record AgentRow(long UserId, string Name, byte StatusValue, int Open, int Overdue);
    private sealed record DayCount(DateTime Day, int Count);
}
