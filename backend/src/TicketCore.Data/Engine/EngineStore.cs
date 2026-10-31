using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using TicketCore.Common;

namespace TicketCore.Data.Engine;

// Data used only by the automation engine (TicketCore.Engine / TicketCore.Worker). The API never calls this.

public sealed record HistoryEvent(long TicketHistoryId, long TicketId, long? UserId, string Activity, string? FromValue, string? ToValue, DateTime OccurredAt);

public sealed class TicketFacts
{
    public long TicketId { get; init; }
    public string TrackingId { get; init; } = string.Empty;
    public int ServiceDeskId { get; init; }
    public int TicketTypeId { get; init; }
    public int PriorityId { get; init; }
    public int TeamId { get; init; }
    public byte StatusValue { get; init; }
    public long? AssigneeUserId { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime CreatedAt { get; init; }
    /// <summary>When support first replied publicly, if ever.</summary>
    public DateTime? FirstSupportReplyAt { get; init; }
    public TicketStatus Status => (TicketStatus)StatusValue;
}

public sealed record CalendarRow(int CalendarId, string Name, string TimeZoneId, bool Is24x7, bool IsDefault);
public sealed record CalendarHoursRow(int CalendarId, byte DayOfWeek, short StartMinute, short EndMinute);
public sealed record CalendarHolidayRow(int CalendarId, DateTime HolidayDate);
public sealed record UnitCalendarRow(int OrgUnitId, int? ParentId, int? CalendarId);

public sealed record SlaPolicyRow(int SlaPolicyId, string Name, int? ServiceDeskId, int? TicketTypeId, int PriorityId,
    int FirstResponseMinutes, int NextResponseMinutes, int ResolutionMinutes, int ReminderMinutes,
    bool EscalateOnFirstResponse, bool EscalateOnNextResponse, bool EscalateOnResolution);
public sealed record EscalationLevelRow(int SlaPolicyId, int Level, int RoleId, string RoleName, int? WindowMinutes);

/// <summary>Database shape of TicketSla (mapped to the engine's SlaState by the engine).</summary>
public sealed class TicketSlaRow
{
    public long TicketId { get; set; }
    public int? SlaPolicyId { get; set; }
    public int? CalendarId { get; set; }
    public DateTime? FirstResponseDueAt { get; set; }
    public DateTime? FirstResponseRemindAt { get; set; }
    public DateTime? FirstResponseMetAt { get; set; }
    public DateTime? FirstResponseRemindedAt { get; set; }
    public DateTime? FirstResponseBreachedAt { get; set; }
    public DateTime? NextResponseDueAt { get; set; }
    public DateTime? NextResponseRemindAt { get; set; }
    public DateTime? NextResponseRemindedAt { get; set; }
    public DateTime? NextResponseBreachedAt { get; set; }
    public DateTime? ResolutionDueAt { get; set; }
    public DateTime? ResolutionRemindAt { get; set; }
    public DateTime? ResolutionMetAt { get; set; }
    public DateTime? ResolutionRemindedAt { get; set; }
    public DateTime? ResolutionBreachedAt { get; set; }
    public DateTime? PausedAt { get; set; }
    public int PausedMinutes { get; set; }
    public DateTime? UnassignedAlert1At { get; set; }
    public DateTime? UnassignedAlert2At { get; set; }
    public DateTime? NextEventAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class EscalationRow
{
    public long TicketEscalationId { get; set; }
    public long TicketId { get; set; }
    public byte Level { get; set; }
    public string Reasons { get; set; } = string.Empty;
    public long? OwnerUserId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? WindowEndsAt { get; set; }
    public DateTime? LastFollowUpAt { get; set; }
    public DateTime? ClearedAt { get; set; }
}

public sealed record TeamSettingsRow(int TeamId, byte AssignmentMode, int MaxOpenTickets, int UnassignedAlertMinutes);

public sealed record UnassignedTicketRow(long TicketId, int TeamId, int PriorityId, DateTime CreatedAt);

public sealed record AgentCandidate(long UserId, int OpenTickets, DateTime? LastAssignedAt);

public sealed record AutoCloseCandidate(long TicketId, DateTime ResolvedAt, int AutoCloseHours);

public interface IEngineStore
{
    // Event stream
    Task<bool> TryLeaseStreamAsync(string name, string owner, DateTime now, TimeSpan lease, CancellationToken ct);
    Task<long> GetCheckpointAsync(string name, CancellationToken ct);
    Task<bool> AdvanceCheckpointAsync(string name, string owner, long from, long to, DateTime now, CancellationToken ct);
    Task<IReadOnlyList<HistoryEvent>> ReadEventsAfterAsync(long afterId, int take, CancellationToken ct);

    // Reference data
    Task<IReadOnlyList<TicketFacts>> GetTicketFactsAsync(IReadOnlyCollection<long> ticketIds, CancellationToken ct);
    Task<(IReadOnlyList<CalendarRow> Calendars, IReadOnlyList<CalendarHoursRow> Hours, IReadOnlyList<CalendarHolidayRow> Holidays, IReadOnlyList<UnitCalendarRow> Units)> GetCalendarDataAsync(CancellationToken ct);
    Task<(IReadOnlyList<SlaPolicyRow> Policies, IReadOnlyList<EscalationLevelRow> Levels)> GetPolicyDataAsync(CancellationToken ct);
    Task<IReadOnlyList<TeamSettingsRow>> GetTeamSettingsAsync(CancellationToken ct);

    /// <summary>Active holders of a role that covers the team, nearest unit first (team, then department, ...).</summary>
    Task<IReadOnlyList<long>> FindNearestRoleHoldersAsync(int teamId, int roleId, CancellationToken ct);

    // SLA state
    Task<TicketSlaRow?> GetSlaAsync(long ticketId, CancellationToken ct);
    Task<bool> InsertSlaAsync(TicketSlaRow row, CancellationToken ct);
    /// <summary>Writes the row only if nobody changed it since it was read (row version). Returns false on conflict.</summary>
    Task<bool> UpdateSlaAsync(TicketSlaRow row, CancellationToken ct);
    /// <summary>Claims rows whose next event is due by pushing a short lease; returns the claimed ticket IDs.</summary>
    Task<IReadOnlyList<long>> ClaimDueSlaAsync(DateTime now, int take, TimeSpan lease, CancellationToken ct);
    Task ReleaseSlaLeaseAsync(long ticketId, CancellationToken ct);

    /// <summary>Open tickets without SLA state whose events have all been read (so the event stream won't start them itself).</summary>
    Task<IReadOnlyList<long>> FindOpenTicketsWithoutSlaAsync(int take, long checkpoint, CancellationToken ct);

    /// <summary>Of the given tickets, those with history the event stream has not processed yet.</summary>
    Task<IReadOnlySet<long>> FindTicketsWithPendingEventsAsync(IReadOnlyCollection<long> ticketIds, long checkpoint, CancellationToken ct);

    Task ReleaseStreamLeaseAsync(string name, string owner, CancellationToken ct);

    // Escalations
    Task<EscalationRow?> GetActiveEscalationAsync(long ticketId, CancellationToken ct);
    /// <summary>Returns false when the ticket already has an active escalation (another worker got there first).</summary>
    Task<bool> InsertEscalationAsync(EscalationRow row, CancellationToken ct);
    /// <summary>Applies only if the escalation is still active and at <paramref name="expectedLevel"/>.</summary>
    Task<bool> UpdateEscalationAsync(EscalationRow row, int expectedLevel, CancellationToken ct);
    Task<IReadOnlyList<long>> FindEscalationsDueAsync(DateTime now, DateTime followUpBefore, int take, CancellationToken ct);

    // Assignment
    Task<IReadOnlyList<UnassignedTicketRow>> GetUnassignedNewTicketsAsync(int take, CancellationToken ct);
    Task<IReadOnlyList<AgentCandidate>> GetAvailableAgentsAsync(int teamId, CancellationToken ct);
    /// <summary>Assigns only if the ticket is still new and unassigned. Writes history in the same transaction.</summary>
    Task<bool> AssignIfStillUnassignedAsync(long ticketId, long userId, string userName, DateTime now, CancellationToken ct);

    // Auto-close
    Task<IReadOnlyList<AutoCloseCandidate>> GetResolvedTicketsAsync(int take, CancellationToken ct);
    Task<bool> CloseIfStillResolvedAsync(long ticketId, DateTime now, CancellationToken ct);

    Task AddHistoryAsync(long ticketId, string activity, string? from, string? to, DateTime now, CancellationToken ct);

    /// <summary>The newest applied database migration script name, for the worker's startup compatibility check.</summary>
    Task<IReadOnlyList<string>> GetAppliedScriptsAsync(CancellationToken ct);
}

public sealed class EngineStore(ISqlConnectionFactory connections) : IEngineStore
{
    public async Task<bool> TryLeaseStreamAsync(string name, string owner, DateTime now, TimeSpan lease, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.EngineCheckpoint SET LeaseOwner = @owner, LeaseUntil = @until
            WHERE Name = @name AND (LeaseUntil IS NULL OR LeaseUntil < @now OR LeaseOwner = @owner)
            """, new { name, owner, now, until = now + lease }, cancellationToken: ct)) == 1;
    }

    public async Task<long> GetCheckpointAsync(string name, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT LastEventId FROM dbo.EngineCheckpoint WHERE Name = @name", new { name }, cancellationToken: ct));
    }

    public async Task<bool> AdvanceCheckpointAsync(string name, string owner, long from, long to, DateTime now, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.EngineCheckpoint SET LastEventId = @to, UpdatedAt = @now
            WHERE Name = @name AND LastEventId = @from AND LeaseOwner = @owner
            """, new { name, owner, from, to, now }, cancellationToken: ct)) == 1;
    }

    public async Task<IReadOnlyList<HistoryEvent>> ReadEventsAfterAsync(long afterId, int take, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<HistoryEvent>(new CommandDefinition("""
            SELECT TOP (@take) TicketHistoryId, TicketId, UserId, Activity, FromValue, ToValue, OccurredAt
            FROM dbo.TicketHistory WHERE TicketHistoryId > @afterId ORDER BY TicketHistoryId
            """, new { afterId, take }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<TicketFacts>> GetTicketFactsAsync(IReadOnlyCollection<long> ticketIds, CancellationToken ct)
    {
        if (ticketIds.Count == 0) return [];
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<TicketFacts>(new CommandDefinition("""
            SELECT TicketId, TrackingId, ServiceDeskId, CAST(TicketTypeId AS INT) AS TicketTypeId, CAST(PriorityId AS INT) AS PriorityId,
                   TeamId, Status AS StatusValue, AssigneeUserId, IsDeleted, CreatedAt,
                   (SELECT MIN(h.OccurredAt) FROM dbo.TicketHistory h WHERE h.TicketId = t.TicketId AND h.Activity = @replied) AS FirstSupportReplyAt
            FROM dbo.Ticket t WHERE TicketId IN @ticketIds
            """, new { ticketIds, replied = TicketActivities.Replied }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<(IReadOnlyList<CalendarRow>, IReadOnlyList<CalendarHoursRow>, IReadOnlyList<CalendarHolidayRow>, IReadOnlyList<UnitCalendarRow>)> GetCalendarDataAsync(CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        var calendars = (await db.QueryAsync<CalendarRow>(new CommandDefinition(
            "SELECT CalendarId, Name, TimeZoneId, Is24x7, IsDefault FROM dbo.Calendar WHERE IsActive = 1", cancellationToken: ct))).ToList();
        var hours = (await db.QueryAsync<CalendarHoursRow>(new CommandDefinition(
            "SELECT CalendarId, DayOfWeek, StartMinute, EndMinute FROM dbo.CalendarHours", cancellationToken: ct))).ToList();
        var holidays = (await db.QueryAsync<CalendarHolidayRow>(new CommandDefinition("""
            SELECT ch.CalendarId, CAST(h.HolidayDate AS DATETIME2) AS HolidayDate
            FROM dbo.CalendarHolidayList ch JOIN dbo.Holiday h ON h.HolidayListId = ch.HolidayListId
            """, cancellationToken: ct))).ToList();
        var units = (await db.QueryAsync<UnitCalendarRow>(new CommandDefinition(
            "SELECT OrgUnitId, ParentId, CalendarId FROM dbo.OrgUnit", cancellationToken: ct))).ToList();
        return (calendars, hours, holidays, units);
    }

    public async Task<(IReadOnlyList<SlaPolicyRow>, IReadOnlyList<EscalationLevelRow>)> GetPolicyDataAsync(CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        var policies = (await db.QueryAsync<SlaPolicyRow>(new CommandDefinition("""
            SELECT SlaPolicyId, Name, ServiceDeskId, CAST(TicketTypeId AS INT) AS TicketTypeId, CAST(PriorityId AS INT) AS PriorityId,
                   FirstResponseMinutes, NextResponseMinutes, ResolutionMinutes, ReminderMinutes,
                   EscalateOnFirstResponse, EscalateOnNextResponse, EscalateOnResolution
            FROM dbo.SlaPolicy WHERE IsActive = 1
            """, cancellationToken: ct))).ToList();
        var levels = (await db.QueryAsync<EscalationLevelRow>(new CommandDefinition("""
            SELECT l.SlaPolicyId, CAST(l.Level AS INT) AS Level, l.RoleId, r.Name AS RoleName, l.WindowMinutes
            FROM dbo.EscalationLevel l JOIN dbo.Role r ON r.RoleId = l.RoleId
            """, cancellationToken: ct))).ToList();
        return (policies, levels);
    }

    public async Task<IReadOnlyList<TeamSettingsRow>> GetTeamSettingsAsync(CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        return (await db.QueryAsync<TeamSettingsRow>(new CommandDefinition(
            "SELECT TeamId, AssignmentMode, MaxOpenTickets, UnassignedAlertMinutes FROM dbo.TeamSettings", cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<long>> FindNearestRoleHoldersAsync(int teamId, int roleId, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<(long UserId, int Depth)>(new CommandDefinition("""
            SELECT ra.UserId, MIN(c.Depth) AS Depth
            FROM dbo.RoleAssignment ra
            JOIN dbo.OrgUnitClosure c ON c.AncestorId = ra.OrgUnitId AND c.DescendantId = @teamId
                                      AND (ra.IncludeDescendants = 1 OR c.Depth = 0)
            JOIN dbo.[User] u ON u.UserId = ra.UserId AND u.IsActive = 1
            WHERE ra.RoleId = @roleId
            GROUP BY ra.UserId
            """, new { teamId, roleId }, cancellationToken: ct));
        var list = rows.ToList();
        if (list.Count == 0) return [];
        var nearest = list.Min(r => r.Depth);
        return list.Where(r => r.Depth == nearest).Select(r => r.UserId).OrderBy(id => id).ToList();
    }

    private const string SlaColumns = """
        TicketId, SlaPolicyId, CalendarId, FirstResponseDueAt, FirstResponseRemindAt, FirstResponseMetAt, FirstResponseRemindedAt,
        FirstResponseBreachedAt, NextResponseDueAt, NextResponseRemindAt, NextResponseRemindedAt, NextResponseBreachedAt,
        ResolutionDueAt, ResolutionRemindAt, ResolutionMetAt, ResolutionRemindedAt, ResolutionBreachedAt, PausedAt, PausedMinutes,
        UnassignedAlert1At, UnassignedAlert2At, NextEventAt
        """;

    public async Task<TicketSlaRow?> GetSlaAsync(long ticketId, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<TicketSlaRow>(new CommandDefinition(
            $"SELECT {SlaColumns}, RowVersion FROM dbo.TicketSla WHERE TicketId = @ticketId", new { ticketId }, cancellationToken: ct));
    }

    public async Task<bool> InsertSlaAsync(TicketSlaRow row, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        try
        {
            await db.ExecuteAsync(new CommandDefinition($"""
                INSERT INTO dbo.TicketSla ({SlaColumns})
                VALUES (@TicketId, @SlaPolicyId, @CalendarId, @FirstResponseDueAt, @FirstResponseRemindAt, @FirstResponseMetAt, @FirstResponseRemindedAt,
                        @FirstResponseBreachedAt, @NextResponseDueAt, @NextResponseRemindAt, @NextResponseRemindedAt, @NextResponseBreachedAt,
                        @ResolutionDueAt, @ResolutionRemindAt, @ResolutionMetAt, @ResolutionRemindedAt, @ResolutionBreachedAt, @PausedAt, @PausedMinutes,
                        @UnassignedAlert1At, @UnassignedAlert2At, @NextEventAt)
                """, row, cancellationToken: ct));
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return false; // already started by another worker
        }
    }

    public async Task<bool> UpdateSlaAsync(TicketSlaRow row, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.TicketSla SET
                SlaPolicyId = @SlaPolicyId, CalendarId = @CalendarId,
                FirstResponseDueAt = @FirstResponseDueAt, FirstResponseRemindAt = @FirstResponseRemindAt, FirstResponseMetAt = @FirstResponseMetAt,
                FirstResponseRemindedAt = @FirstResponseRemindedAt, FirstResponseBreachedAt = @FirstResponseBreachedAt,
                NextResponseDueAt = @NextResponseDueAt, NextResponseRemindAt = @NextResponseRemindAt,
                NextResponseRemindedAt = @NextResponseRemindedAt, NextResponseBreachedAt = @NextResponseBreachedAt,
                ResolutionDueAt = @ResolutionDueAt, ResolutionRemindAt = @ResolutionRemindAt, ResolutionMetAt = @ResolutionMetAt,
                ResolutionRemindedAt = @ResolutionRemindedAt, ResolutionBreachedAt = @ResolutionBreachedAt,
                PausedAt = @PausedAt, PausedMinutes = @PausedMinutes,
                UnassignedAlert1At = @UnassignedAlert1At, UnassignedAlert2At = @UnassignedAlert2At,
                NextEventAt = @NextEventAt, LeaseUntil = NULL
            WHERE TicketId = @TicketId AND RowVersion = @RowVersion
            """, row, cancellationToken: ct)) == 1;
    }

    public async Task<IReadOnlyList<long>> ClaimDueSlaAsync(DateTime now, int take, TimeSpan lease, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        var ids = await db.QueryAsync<long>(new CommandDefinition("""
            WITH due AS (
                SELECT TOP (@take) TicketId, LeaseUntil FROM dbo.TicketSla WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE NextEventAt <= @now AND (LeaseUntil IS NULL OR LeaseUntil < @now)
                ORDER BY NextEventAt
            )
            UPDATE due SET LeaseUntil = @until
            OUTPUT inserted.TicketId
            """, new { now, take, until = now + lease }, cancellationToken: ct));
        return ids.ToList();
    }

    public async Task ReleaseSlaLeaseAsync(long ticketId, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.TicketSla SET LeaseUntil = NULL WHERE TicketId = @ticketId", new { ticketId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<long>> FindOpenTicketsWithoutSlaAsync(int take, long checkpoint, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        var ids = await db.QueryAsync<long>(new CommandDefinition("""
            SELECT TOP (@take) t.TicketId FROM dbo.Ticket t
            LEFT JOIN dbo.TicketSla s ON s.TicketId = t.TicketId
            WHERE s.TicketId IS NULL AND t.IsDeleted = 0 AND t.Status NOT IN (6, 8)
              AND NOT EXISTS (SELECT 1 FROM dbo.TicketHistory h WHERE h.TicketId = t.TicketId AND h.TicketHistoryId > @checkpoint)
            ORDER BY t.TicketId
            """, new { take, checkpoint }, cancellationToken: ct));
        return ids.ToList();
    }

    public async Task<IReadOnlySet<long>> FindTicketsWithPendingEventsAsync(IReadOnlyCollection<long> ticketIds, long checkpoint, CancellationToken ct)
    {
        if (ticketIds.Count == 0) return new HashSet<long>();
        await using var db = await connections.OpenAsync(ct);
        var ids = await db.QueryAsync<long>(new CommandDefinition("""
            SELECT DISTINCT TicketId FROM dbo.TicketHistory WHERE TicketId IN @ticketIds AND TicketHistoryId > @checkpoint
            """, new { ticketIds, checkpoint }, cancellationToken: ct));
        return ids.ToHashSet();
    }

    public async Task ReleaseStreamLeaseAsync(string name, string owner, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.EngineCheckpoint SET LeaseOwner = NULL, LeaseUntil = NULL WHERE Name = @name AND LeaseOwner = @owner",
            new { name, owner }, cancellationToken: ct));
    }

    private const string EscalationColumns = "TicketEscalationId, TicketId, Level, Reasons, OwnerUserId, StartedAt, WindowEndsAt, LastFollowUpAt, ClearedAt";

    public async Task<EscalationRow?> GetActiveEscalationAsync(long ticketId, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<EscalationRow>(new CommandDefinition(
            $"SELECT {EscalationColumns} FROM dbo.TicketEscalation WHERE TicketId = @ticketId AND ClearedAt IS NULL",
            new { ticketId }, cancellationToken: ct));
    }

    public async Task<bool> InsertEscalationAsync(EscalationRow row, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        try
        {
            row.TicketEscalationId = await db.ExecuteScalarAsync<long>(new CommandDefinition("""
                INSERT INTO dbo.TicketEscalation (TicketId, Level, Reasons, OwnerUserId, StartedAt, WindowEndsAt)
                OUTPUT inserted.TicketEscalationId
                VALUES (@TicketId, @Level, @Reasons, @OwnerUserId, @StartedAt, @WindowEndsAt)
                """, row, cancellationToken: ct));
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return false;
        }
    }

    public async Task<bool> UpdateEscalationAsync(EscalationRow row, int expectedLevel, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.TicketEscalation
            SET Level = @Level, Reasons = @Reasons, OwnerUserId = @OwnerUserId, StartedAt = @StartedAt,
                WindowEndsAt = @WindowEndsAt, LastFollowUpAt = @LastFollowUpAt, ClearedAt = @ClearedAt
            WHERE TicketEscalationId = @TicketEscalationId AND ClearedAt IS NULL AND Level = @expectedLevel
            """, new
        {
            row.Level, row.Reasons, row.OwnerUserId, row.StartedAt, row.WindowEndsAt, row.LastFollowUpAt, row.ClearedAt,
            row.TicketEscalationId, expectedLevel
        }, cancellationToken: ct)) == 1;
    }

    public async Task<IReadOnlyList<long>> FindEscalationsDueAsync(DateTime now, DateTime followUpBefore, int take, CancellationToken ct)
    {
        // Window ran out (move up), or at the final level (no window) and not followed up recently.
        await using var db = await connections.OpenAsync(ct);
        var ids = await db.QueryAsync<long>(new CommandDefinition("""
            SELECT TOP (@take) TicketId FROM dbo.TicketEscalation
            WHERE ClearedAt IS NULL
              AND ((WindowEndsAt IS NOT NULL AND WindowEndsAt <= @now)
                   OR (WindowEndsAt IS NULL AND ISNULL(LastFollowUpAt, StartedAt) <= @followUpBefore))
            ORDER BY StartedAt
            """, new { now, followUpBefore, take }, cancellationToken: ct));
        return ids.ToList();
    }

    public async Task<IReadOnlyList<UnassignedTicketRow>> GetUnassignedNewTicketsAsync(int take, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<UnassignedTicketRow>(new CommandDefinition("""
            SELECT TOP (@take) TicketId, TeamId, CAST(PriorityId AS INT) AS PriorityId, CreatedAt
            FROM dbo.Ticket
            WHERE IsDeleted = 0 AND Status = 1 AND AssigneeUserId IS NULL
            ORDER BY PriorityId, CreatedAt
            """, new { take }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<AgentCandidate>> GetAvailableAgentsAsync(int teamId, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<AgentCandidate>(new CommandDefinition("""
            SELECT u.UserId,
                   (SELECT COUNT(1) FROM dbo.Ticket t
                    WHERE t.AssigneeUserId = u.UserId AND t.IsDeleted = 0 AND t.Status IN (1, 2, 3, 4)) AS OpenTickets,
                   av.LastAssignedAt
            FROM dbo.[User] u
            JOIN dbo.AgentAvailability av ON av.UserId = u.UserId AND av.Status = 1
            WHERE u.IsActive = 1 AND EXISTS (
                SELECT 1 FROM dbo.RoleAssignment ra
                JOIN dbo.RolePermission rp ON rp.RoleId = ra.RoleId AND rp.PermissionKey = @permission
                JOIN dbo.OrgUnitClosure c ON c.AncestorId = ra.OrgUnitId AND c.DescendantId = @teamId
                                          AND (ra.IncludeDescendants = 1 OR c.Depth = 0)
                WHERE ra.UserId = u.UserId)
            """, new { teamId, permission = Permissions.AgentReceiveAuto }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> AssignIfStillUnassignedAsync(long ticketId, long userId, string userName, DateTime now, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var updated = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.Ticket SET AssigneeUserId = @userId, Status = 2, UpdatedAt = @now
            WHERE TicketId = @ticketId AND AssigneeUserId IS NULL AND Status = 1 AND IsDeleted = 0
            """, new { ticketId, userId, now }, tx, cancellationToken: ct));
        if (updated != 1)
        {
            await tx.RollbackAsync(ct);
            return false;
        }
        await InsertHistoryAsync(db, tx, ticketId, TicketActivities.Assigned, null, userName, now, ct);
        await InsertHistoryAsync(db, tx, ticketId, TicketActivities.StatusChanged, nameof(TicketStatus.New), nameof(TicketStatus.Open), now, ct);
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.AgentAvailability SET LastAssignedAt = @now WHERE UserId = @userId", new { userId, now }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<AutoCloseCandidate>> GetResolvedTicketsAsync(int take, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<AutoCloseCandidate>(new CommandDefinition("""
            SELECT TOP (@take) t.TicketId, t.ResolvedAt, d.AutoCloseHours
            FROM dbo.Ticket t JOIN dbo.ServiceDesk d ON d.ServiceDeskId = t.ServiceDeskId
            WHERE t.Status = 5 AND t.IsDeleted = 0 AND t.ResolvedAt IS NOT NULL AND d.AutoCloseHours > 0
            ORDER BY t.ResolvedAt
            """, new { take }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> CloseIfStillResolvedAsync(long ticketId, DateTime now, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var updated = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.Ticket SET Status = 6, ClosedAt = @now, UpdatedAt = @now
            WHERE TicketId = @ticketId AND Status = 5 AND IsDeleted = 0
            """, new { ticketId, now }, tx, cancellationToken: ct));
        if (updated != 1)
        {
            await tx.RollbackAsync(ct);
            return false;
        }
        await InsertHistoryAsync(db, tx, ticketId, TicketActivities.StatusChanged, nameof(TicketStatus.Resolved), nameof(TicketStatus.Closed), now, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task AddHistoryAsync(long ticketId, string activity, string? from, string? to, DateTime now, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await InsertHistoryAsync(db, tx, ticketId, activity, from, to, now, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetAppliedScriptsAsync(CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        return (await db.QueryAsync<string>(new CommandDefinition("SELECT ScriptName FROM dbo.SchemaVersions", cancellationToken: ct))).ToList();
    }

    /// <summary>System activity: no user. The engine writes these and ignores them when it reads the stream back.</summary>
    private static Task InsertHistoryAsync(DbConnection db, DbTransaction tx, long ticketId, string activity, string? from, string? to, DateTime now, CancellationToken ct) =>
        db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.TicketHistory (TicketId, UserId, Activity, FromValue, ToValue, OccurredAt)
            VALUES (@ticketId, NULL, @activity, @from, @to, @now)
            """, new { ticketId, activity, from, to, now }, tx, cancellationToken: ct));
}
