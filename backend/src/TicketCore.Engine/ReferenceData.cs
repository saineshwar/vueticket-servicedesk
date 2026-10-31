using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketCore.Common;
using TicketCore.Data.Engine;
using TicketCore.Engine.Calendars;
using TicketCore.Engine.Sla;

namespace TicketCore.Engine;

/// <summary>Assignment settings for a team; teams without a row use these defaults.</summary>
public sealed record TeamAssignmentSettings(int TeamId, AssignmentMode Mode, int MaxOpenTickets, int UnassignedAlertMinutes)
{
    public static TeamAssignmentSettings Default(int teamId) => new(teamId, AssignmentMode.LoadBalanced, 20, 15);
}

/// <summary>
/// Calendars, SLA policies and team settings, loaded from the database and refreshed periodically,
/// so admin changes reach a running worker without a restart.
/// </summary>
public sealed class ReferenceData(IServiceScopeFactory scopes, IOptions<EngineOptions> options, TimeProvider clock, ILogger<ReferenceData> logger)
{
    private sealed record Snapshot(
        IReadOnlyDictionary<int, BusinessCalendar> Calendars,
        int? DefaultCalendarId,
        IReadOnlyDictionary<int, (int? ParentId, int? CalendarId)> Units,
        IReadOnlyList<SlaPolicyDefinition> Policies,
        IReadOnlyDictionary<int, TeamAssignmentSettings> Teams,
        DateTime LoadedAt);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Snapshot? _snapshot;

    /// <summary>The calendar that applies to a team: its own, else the nearest ancestor's, else the default.</summary>
    public async Task<(int? CalendarId, BusinessCalendar Calendar)> CalendarForTeamAsync(int teamId, CancellationToken ct)
    {
        var s = await GetAsync(ct);
        int? unit = teamId;
        for (var guard = 0; unit is { } id && guard < 64; guard++)
        {
            if (!s.Units.TryGetValue(id, out var u)) break;
            if (u.CalendarId is { } cid && s.Calendars.TryGetValue(cid, out var cal)) return (cid, cal);
            unit = u.ParentId;
        }
        if (s.DefaultCalendarId is { } def && s.Calendars.TryGetValue(def, out var defCal)) return (def, defCal);
        return (null, BusinessCalendar.AlwaysOpen);
    }

    public async Task<BusinessCalendar> CalendarAsync(int? calendarId, int teamId, CancellationToken ct)
    {
        var s = await GetAsync(ct);
        if (calendarId is { } id && s.Calendars.TryGetValue(id, out var cal)) return cal;
        return (await CalendarForTeamAsync(teamId, ct)).Calendar;
    }

    public async Task<SlaPolicyDefinition?> PolicyForAsync(int serviceDeskId, int ticketTypeId, int priorityId, CancellationToken ct) =>
        SlaRules.SelectPolicy((await GetAsync(ct)).Policies, serviceDeskId, ticketTypeId, priorityId);

    public async Task<SlaPolicyDefinition?> PolicyByIdAsync(int? slaPolicyId, CancellationToken ct) =>
        slaPolicyId is { } id ? (await GetAsync(ct)).Policies.FirstOrDefault(p => p.SlaPolicyId == id) : null;

    public async Task<TeamAssignmentSettings> TeamSettingsAsync(int teamId, CancellationToken ct) =>
        (await GetAsync(ct)).Teams.TryGetValue(teamId, out var t) ? t : TeamAssignmentSettings.Default(teamId);

    private async Task<Snapshot> GetAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var current = _snapshot;
        if (current is not null && (now - current.LoadedAt).TotalSeconds < options.Value.ReferenceDataRefreshSeconds) return current;

        await _lock.WaitAsync(ct);
        try
        {
            if (_snapshot is not null && (now - _snapshot.LoadedAt).TotalSeconds < options.Value.ReferenceDataRefreshSeconds) return _snapshot;
            _snapshot = await LoadAsync(now, ct);
            return _snapshot;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Snapshot> LoadAsync(DateTime now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IEngineStore>();

        var (calendars, hours, holidays, units) = await store.GetCalendarDataAsync(ct);
        var built = new Dictionary<int, BusinessCalendar>();
        foreach (var c in calendars)
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId);
                var windows = hours.Where(h => h.CalendarId == c.CalendarId)
                    .Select(h => new WorkingWindow((DayOfWeek)h.DayOfWeek, h.StartMinute, h.EndMinute));
                var days = holidays.Where(h => h.CalendarId == c.CalendarId).Select(h => DateOnly.FromDateTime(h.HolidayDate));
                built[c.CalendarId] = new BusinessCalendar(zone, c.Is24x7, windows, days);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            {
                logger.LogError(ex, "Calendar {Calendar} is invalid and is ignored; teams using it fall back to the default", c.Name);
            }
        }

        var (policies, levels) = await store.GetPolicyDataAsync(ct);
        var definitions = policies.Select(p => new SlaPolicyDefinition(
            p.SlaPolicyId, p.Name, p.ServiceDeskId, p.TicketTypeId, p.PriorityId,
            p.FirstResponseMinutes, p.NextResponseMinutes, p.ResolutionMinutes, p.ReminderMinutes,
            p.EscalateOnFirstResponse, p.EscalateOnNextResponse, p.EscalateOnResolution,
            levels.Where(l => l.SlaPolicyId == p.SlaPolicyId).OrderBy(l => l.Level)
                .Select(l => new EscalationLevelDefinition(l.Level, l.RoleId, l.RoleName, l.WindowMinutes)).ToList())).ToList();

        var teams = (await store.GetTeamSettingsAsync(ct)).ToDictionary(
            t => t.TeamId, t => new TeamAssignmentSettings(t.TeamId, (AssignmentMode)t.AssignmentMode, t.MaxOpenTickets, t.UnassignedAlertMinutes));

        return new Snapshot(
            built,
            calendars.FirstOrDefault(c => c.IsDefault && built.ContainsKey(c.CalendarId))?.CalendarId,
            units.ToDictionary(u => u.OrgUnitId, u => (u.ParentId, u.CalendarId)),
            definitions, teams, now);
    }
}
