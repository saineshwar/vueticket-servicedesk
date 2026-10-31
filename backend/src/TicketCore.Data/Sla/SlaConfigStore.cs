using Dapper;
using TicketCore.ViewModels.Sla;

namespace TicketCore.Data.Sla;

public sealed record CalendarValues(string Name, string TimeZoneId, bool Is24x7, bool IsDefault, bool IsActive,
    IReadOnlyList<CalendarHoursDto> Hours, IReadOnlyList<int> HolidayListIds);

public sealed record HolidayValues(DateTime Date, string Name);

public sealed record SlaPolicyValues(string Name, int? ServiceDeskId, int? TicketTypeId, int PriorityId,
    int FirstResponseMinutes, int NextResponseMinutes, int ResolutionMinutes, int ReminderMinutes,
    bool EscalateOnFirstResponse, bool EscalateOnNextResponse, bool EscalateOnResolution, bool IsActive,
    IReadOnlyList<EscalationLevelDto> Levels);

public sealed class UnitSettingsRow
{
    public int OrgUnitId { get; init; }
    public int? ParentId { get; init; }
    public byte UnitTypeValue { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int? CalendarId { get; init; }
    public byte? AssignmentMode { get; init; }
    public int? MaxOpenTickets { get; init; }
    public int? UnassignedAlertMinutes { get; init; }
}

/// <summary>
/// Calendars, holidays, SLA policies and per-unit settings, as admins edit them. The engine reads the same tables
/// through its own store and picks changes up on its next reference-data refresh.
/// </summary>
public interface ISlaConfigStore
{
    Task<IReadOnlyList<CalendarDto>> ListCalendarsAsync(CancellationToken ct = default);
    Task<bool> CalendarNameExistsAsync(string name, int? excludeId, CancellationToken ct = default);
    Task<int> SaveCalendarAsync(int? calendarId, CalendarValues values, CancellationToken ct = default);

    Task<IReadOnlyList<HolidayListDto>> ListHolidayListsAsync(CancellationToken ct = default);
    Task<bool> HolidayListNameExistsAsync(string name, int? excludeId, CancellationToken ct = default);
    Task<int> SaveHolidayListAsync(int? holidayListId, string name, IReadOnlyList<HolidayValues> holidays, CancellationToken ct = default);

    Task<IReadOnlyList<SlaPolicyDto>> ListPoliciesAsync(CancellationToken ct = default);
    /// <summary>Whether another active policy already covers this desk / type / priority combination.</summary>
    Task<string?> ActiveScopeTakenByAsync(int? serviceDeskId, int? ticketTypeId, int priorityId, int? excludeId, CancellationToken ct = default);
    Task<int> SavePolicyAsync(int? slaPolicyId, SlaPolicyValues values, CancellationToken ct = default);

    Task<IReadOnlyList<UnitSettingsRow>> ListUnitSettingsAsync(CancellationToken ct = default);
    /// <summary>Assignment settings are written only when given (teams).</summary>
    Task SaveUnitSettingsAsync(int orgUnitId, int? calendarId, (byte Mode, int MaxOpen, int AlertMinutes)? assignment, CancellationToken ct = default);
}

public sealed class SlaConfigStore(ISqlConnectionFactory connections) : ISlaConfigStore
{
    private sealed record CalendarRow(int CalendarId, string Name, string TimeZoneId, bool Is24x7, bool IsDefault, bool IsActive, int UnitsUsing);
    private sealed record HoursRow(int CalendarId, byte DayOfWeek, short StartMinute, short EndMinute);
    private sealed record LinkRow(int CalendarId, int HolidayListId);
    private sealed record HolidayRow(int HolidayListId, DateTime HolidayDate, string Name);
    private sealed record ListRow(int HolidayListId, string Name);
    private sealed record LevelRow(int SlaPolicyId, byte Level, int RoleId, string RoleName, int? WindowMinutes);

    private sealed class PolicyRow
    {
        public int SlaPolicyId { get; init; }
        public string Name { get; init; } = string.Empty;
        public int? ServiceDeskId { get; init; }
        public string? ServiceDeskName { get; init; }
        public int? TicketTypeId { get; init; }
        public string? TicketTypeName { get; init; }
        public int PriorityId { get; init; }
        public string PriorityName { get; init; } = string.Empty;
        public int FirstResponseMinutes { get; init; }
        public int NextResponseMinutes { get; init; }
        public int ResolutionMinutes { get; init; }
        public int ReminderMinutes { get; init; }
        public bool EscalateOnFirstResponse { get; init; }
        public bool EscalateOnNextResponse { get; init; }
        public bool EscalateOnResolution { get; init; }
        public bool IsActive { get; init; }
    }

    public async Task<IReadOnlyList<CalendarDto>> ListCalendarsAsync(CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        using var grid = await db.QueryMultipleAsync(new CommandDefinition("""
            SELECT c.CalendarId, c.Name, c.TimeZoneId, c.Is24x7, c.IsDefault, c.IsActive,
                   (SELECT COUNT(1) FROM dbo.OrgUnit o WHERE o.CalendarId = c.CalendarId) AS UnitsUsing
            FROM dbo.Calendar c ORDER BY c.IsDefault DESC, c.Name;
            SELECT CalendarId, DayOfWeek, StartMinute, EndMinute FROM dbo.CalendarHours ORDER BY DayOfWeek, StartMinute;
            SELECT CalendarId, HolidayListId FROM dbo.CalendarHolidayList;
            """, cancellationToken: ct));
        var calendars = await grid.ReadAsync<CalendarRow>();
        var hours = (await grid.ReadAsync<HoursRow>()).ToLookup(h => h.CalendarId);
        var links = (await grid.ReadAsync<LinkRow>()).ToLookup(l => l.CalendarId);
        return calendars.Select(c => new CalendarDto(c.CalendarId, c.Name, c.TimeZoneId, c.Is24x7, c.IsDefault, c.IsActive, c.UnitsUsing,
            hours[c.CalendarId].Select(h => new CalendarHoursDto(h.DayOfWeek, h.StartMinute, h.EndMinute)).ToList(),
            links[c.CalendarId].Select(l => l.HolidayListId).ToList())).ToList();
    }

    public async Task<bool> CalendarNameExistsAsync(string name, int? excludeId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Calendar WHERE Name = @name AND (@excludeId IS NULL OR CalendarId <> @excludeId)) THEN 1 ELSE 0 END",
            new { name, excludeId }, cancellationToken: ct));
    }

    public async Task<int> SaveCalendarAsync(int? calendarId, CalendarValues v, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        // Only one default calendar is allowed (unique filtered index), so clear the old one first.
        if (v.IsDefault)
            await db.ExecuteAsync(new CommandDefinition("UPDATE dbo.Calendar SET IsDefault = 0 WHERE IsDefault = 1 AND (@calendarId IS NULL OR CalendarId <> @calendarId)",
                new { calendarId }, tx, cancellationToken: ct));

        var id = calendarId ?? 0;
        var args = new { calendarId, v.Name, v.TimeZoneId, v.Is24x7, v.IsDefault, v.IsActive };
        if (calendarId is null)
            id = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
                INSERT INTO dbo.Calendar (Name, TimeZoneId, Is24x7, IsDefault, IsActive) OUTPUT inserted.CalendarId
                VALUES (@Name, @TimeZoneId, @Is24x7, @IsDefault, @IsActive)
                """, args, tx, cancellationToken: ct));
        else
            await db.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.Calendar SET Name = @Name, TimeZoneId = @TimeZoneId, Is24x7 = @Is24x7, IsDefault = @IsDefault, IsActive = @IsActive
                WHERE CalendarId = @calendarId
                """, args, tx, cancellationToken: ct));

        await db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.CalendarHours WHERE CalendarId = @id; DELETE FROM dbo.CalendarHolidayList WHERE CalendarId = @id;",
            new { id }, tx, cancellationToken: ct));
        if (v.Hours.Count > 0)
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.CalendarHours (CalendarId, DayOfWeek, StartMinute, EndMinute) VALUES (@id, @DayOfWeek, @StartMinute, @EndMinute)",
                v.Hours.Select(h => new { id, h.DayOfWeek, h.StartMinute, h.EndMinute }), tx, cancellationToken: ct));
        if (v.HolidayListIds.Count > 0)
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.CalendarHolidayList (CalendarId, HolidayListId) VALUES (@id, @listId)",
                v.HolidayListIds.Select(listId => new { id, listId }), tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<IReadOnlyList<HolidayListDto>> ListHolidayListsAsync(CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        using var grid = await db.QueryMultipleAsync(new CommandDefinition("""
            SELECT HolidayListId, Name FROM dbo.HolidayList ORDER BY Name;
            SELECT HolidayListId, HolidayDate, Name FROM dbo.Holiday ORDER BY HolidayDate;
            """, cancellationToken: ct));
        var lists = await grid.ReadAsync<ListRow>();
        var days = (await grid.ReadAsync<HolidayRow>()).ToLookup(h => h.HolidayListId);
        return lists.Select(l => new HolidayListDto(l.HolidayListId, l.Name,
            days[l.HolidayListId].Select(h => new HolidayDto(h.HolidayDate.ToString("yyyy-MM-dd"), h.Name)).ToList())).ToList();
    }

    public async Task<bool> HolidayListNameExistsAsync(string name, int? excludeId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.HolidayList WHERE Name = @name AND (@excludeId IS NULL OR HolidayListId <> @excludeId)) THEN 1 ELSE 0 END",
            new { name, excludeId }, cancellationToken: ct));
    }

    public async Task<int> SaveHolidayListAsync(int? holidayListId, string name, IReadOnlyList<HolidayValues> holidays, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var id = holidayListId ?? await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "INSERT INTO dbo.HolidayList (Name) OUTPUT inserted.HolidayListId VALUES (@name)", new { name }, tx, cancellationToken: ct));
        if (holidayListId is not null)
            await db.ExecuteAsync(new CommandDefinition("UPDATE dbo.HolidayList SET Name = @name WHERE HolidayListId = @id", new { id, name }, tx, cancellationToken: ct));

        await db.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.Holiday WHERE HolidayListId = @id", new { id }, tx, cancellationToken: ct));
        if (holidays.Count > 0)
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.Holiday (HolidayListId, HolidayDate, Name) VALUES (@id, @Date, @Name)",
                holidays.Select(h => new { id, h.Date, h.Name }), tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<IReadOnlyList<SlaPolicyDto>> ListPoliciesAsync(CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        using var grid = await db.QueryMultipleAsync(new CommandDefinition("""
            SELECT p.SlaPolicyId, p.Name, p.ServiceDeskId, d.Name AS ServiceDeskName,
                   CAST(p.TicketTypeId AS INT) AS TicketTypeId, tt.Name AS TicketTypeName,
                   CAST(p.PriorityId AS INT) AS PriorityId, pr.Name AS PriorityName,
                   p.FirstResponseMinutes, p.NextResponseMinutes, p.ResolutionMinutes, p.ReminderMinutes,
                   p.EscalateOnFirstResponse, p.EscalateOnNextResponse, p.EscalateOnResolution, p.IsActive
            FROM dbo.SlaPolicy p
            JOIN dbo.Priority pr ON pr.PriorityId = p.PriorityId
            LEFT JOIN dbo.ServiceDesk d ON d.ServiceDeskId = p.ServiceDeskId
            LEFT JOIN dbo.TicketType tt ON tt.TicketTypeId = p.TicketTypeId
            ORDER BY p.IsActive DESC, CASE WHEN p.ServiceDeskId IS NULL THEN 0 ELSE 1 END, d.Name, p.PriorityId, p.Name;
            SELECT l.SlaPolicyId, l.Level, l.RoleId, r.Name AS RoleName, l.WindowMinutes
            FROM dbo.EscalationLevel l JOIN dbo.Role r ON r.RoleId = l.RoleId ORDER BY l.Level;
            """, cancellationToken: ct));
        var policies = await grid.ReadAsync<PolicyRow>();
        var levels = (await grid.ReadAsync<LevelRow>()).ToLookup(l => l.SlaPolicyId);
        return policies.Select(p => new SlaPolicyDto(p.SlaPolicyId, p.Name, p.ServiceDeskId, p.ServiceDeskName, p.TicketTypeId, p.TicketTypeName,
            p.PriorityId, p.PriorityName, p.FirstResponseMinutes, p.NextResponseMinutes, p.ResolutionMinutes, p.ReminderMinutes,
            p.EscalateOnFirstResponse, p.EscalateOnNextResponse, p.EscalateOnResolution, p.IsActive,
            levels[p.SlaPolicyId].Select(l => new EscalationLevelDto(l.Level, l.RoleId, l.RoleName, l.WindowMinutes)).ToList())).ToList();
    }

    public async Task<string?> ActiveScopeTakenByAsync(int? serviceDeskId, int? ticketTypeId, int priorityId, int? excludeId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteScalarAsync<string?>(new CommandDefinition("""
            SELECT TOP (1) Name FROM dbo.SlaPolicy
            WHERE IsActive = 1 AND PriorityId = @priorityId
              AND ((ServiceDeskId IS NULL AND @serviceDeskId IS NULL) OR ServiceDeskId = @serviceDeskId)
              AND ((TicketTypeId IS NULL AND @ticketTypeId IS NULL) OR TicketTypeId = @ticketTypeId)
              AND (@excludeId IS NULL OR SlaPolicyId <> @excludeId)
            """, new { serviceDeskId, ticketTypeId, priorityId, excludeId }, cancellationToken: ct));
    }

    public async Task<int> SavePolicyAsync(int? slaPolicyId, SlaPolicyValues v, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var args = new
        {
            slaPolicyId, v.Name, v.ServiceDeskId, v.TicketTypeId, v.PriorityId, v.FirstResponseMinutes, v.NextResponseMinutes,
            v.ResolutionMinutes, v.ReminderMinutes, v.EscalateOnFirstResponse, v.EscalateOnNextResponse, v.EscalateOnResolution, v.IsActive
        };
        var id = slaPolicyId ?? await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO dbo.SlaPolicy (Name, ServiceDeskId, TicketTypeId, PriorityId, FirstResponseMinutes, NextResponseMinutes, ResolutionMinutes,
                                       ReminderMinutes, EscalateOnFirstResponse, EscalateOnNextResponse, EscalateOnResolution, IsActive)
            OUTPUT inserted.SlaPolicyId
            VALUES (@Name, @ServiceDeskId, @TicketTypeId, @PriorityId, @FirstResponseMinutes, @NextResponseMinutes, @ResolutionMinutes,
                    @ReminderMinutes, @EscalateOnFirstResponse, @EscalateOnNextResponse, @EscalateOnResolution, @IsActive)
            """, args, tx, cancellationToken: ct));
        if (slaPolicyId is not null)
            await db.ExecuteAsync(new CommandDefinition("""
                UPDATE dbo.SlaPolicy
                SET Name = @Name, ServiceDeskId = @ServiceDeskId, TicketTypeId = @TicketTypeId, PriorityId = @PriorityId,
                    FirstResponseMinutes = @FirstResponseMinutes, NextResponseMinutes = @NextResponseMinutes, ResolutionMinutes = @ResolutionMinutes,
                    ReminderMinutes = @ReminderMinutes, EscalateOnFirstResponse = @EscalateOnFirstResponse,
                    EscalateOnNextResponse = @EscalateOnNextResponse, EscalateOnResolution = @EscalateOnResolution, IsActive = @IsActive
                WHERE SlaPolicyId = @slaPolicyId
                """, args, tx, cancellationToken: ct));

        await db.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.EscalationLevel WHERE SlaPolicyId = @id", new { id }, tx, cancellationToken: ct));
        if (v.Levels.Count > 0)
            await db.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.EscalationLevel (SlaPolicyId, Level, RoleId, WindowMinutes) VALUES (@id, @Level, @RoleId, @WindowMinutes)",
                v.Levels.Select(l => new { id, l.Level, l.RoleId, l.WindowMinutes }), tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<IReadOnlyList<UnitSettingsRow>> ListUnitSettingsAsync(CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<UnitSettingsRow>(new CommandDefinition("""
            SELECT o.OrgUnitId, o.ParentId, o.UnitType AS UnitTypeValue, o.Name, o.Code, o.IsActive, o.CalendarId,
                   s.AssignmentMode, s.MaxOpenTickets, s.UnassignedAlertMinutes
            FROM dbo.OrgUnit o
            LEFT JOIN dbo.TeamSettings s ON s.TeamId = o.OrgUnitId
            ORDER BY o.Name
            """, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task SaveUnitSettingsAsync(int orgUnitId, int? calendarId, (byte Mode, int MaxOpen, int AlertMinutes)? assignment, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("UPDATE dbo.OrgUnit SET CalendarId = @calendarId WHERE OrgUnitId = @orgUnitId",
            new { orgUnitId, calendarId }, tx, cancellationToken: ct));
        if (assignment is { } a)
            await db.ExecuteAsync(new CommandDefinition("""
                MERGE dbo.TeamSettings WITH (HOLDLOCK) AS t
                USING (SELECT @orgUnitId AS TeamId) AS s ON t.TeamId = s.TeamId
                WHEN MATCHED THEN UPDATE SET AssignmentMode = @mode, MaxOpenTickets = @maxOpen, UnassignedAlertMinutes = @alert
                WHEN NOT MATCHED THEN INSERT (TeamId, AssignmentMode, MaxOpenTickets, UnassignedAlertMinutes) VALUES (@orgUnitId, @mode, @maxOpen, @alert);
                """, new { orgUnitId, mode = a.Mode, maxOpen = a.MaxOpen, alert = a.AlertMinutes }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }
}
