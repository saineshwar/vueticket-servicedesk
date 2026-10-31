using System.Globalization;
using TicketCore.Common;
using TicketCore.Core.Access;
using TicketCore.Data.Access.Queries;
using TicketCore.Data.Audit;
using TicketCore.Data.ServiceDesks.Queries;
using TicketCore.Data.Sla;
using TicketCore.ViewModels.ServiceDesks;
using TicketCore.ViewModels.Sla;

namespace TicketCore.Core.Sla;

public interface ISlaConfigService
{
    Task<SlaOptionsResponse> GetOptionsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CalendarDto>> ListCalendarsAsync(CancellationToken ct = default);
    Task<OperationResult<CalendarDto>> SaveCalendarAsync(int? calendarId, SaveCalendarRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<HolidayListDto>> ListHolidayListsAsync(CancellationToken ct = default);
    Task<OperationResult<HolidayListDto>> SaveHolidayListAsync(int? holidayListId, SaveHolidayListRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<SlaPolicyDto>> ListPoliciesAsync(CancellationToken ct = default);
    Task<OperationResult<SlaPolicyDto>> SavePolicyAsync(int? slaPolicyId, SaveSlaPolicyRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<UnitSettingsDto>> ListUnitSettingsAsync(CancellationToken ct = default);
    Task<OperationResult<UnitSettingsDto>> SaveUnitSettingsAsync(int orgUnitId, SaveUnitSettingsRequest request, CancellationToken ct = default);
}

/// <summary>
/// Calendars, holidays and organisation-wide SLA policies need the permission on the organisation itself.
/// A desk's own policies need config.sla on the desk's default team. A unit's business hours need config.calendars
/// on that unit, and a team's assignment settings config.routing on that team.
/// Changes apply to new SLA clocks; tickets already being tracked keep the targets they started with.
/// </summary>
public sealed class SlaConfigService(
    ISlaConfigStore store,
    IServiceDeskQueries desks,
    IRoleQueries roles,
    IPermissionEvaluator permissions,
    IAuditCommand audit,
    ICurrentUser currentUser) : ISlaConfigService
{
    private const int MaxTargetMinutes = 525_600; // a year of business minutes is far beyond any real target
    private const int MaxLevels = 4;

    public async Task<SlaOptionsResponse> GetOptionsAsync(CancellationToken ct = default)
    {
        var lookups = await desks.GetLookupsAsync(ct);
        var deskList = await desks.ListAsync(activeOnly: false, ct);
        var roleList = await roles.ListAsync(ct);
        var calendars = await store.ListCalendarsAsync(ct);
        return new SlaOptionsResponse(
            deskList.Select(d => new LookupItem(d.ServiceDeskId, d.Name)).ToList(),
            lookups.TicketTypes, lookups.Priorities,
            roleList.Select(r => new LookupItem(r.RoleId, r.Name)).ToList(),
            calendars.Where(c => c.IsActive).Select(c => new LookupItem(c.CalendarId, c.Name)).ToList());
    }

    // ---- Calendars ----

    public Task<IReadOnlyList<CalendarDto>> ListCalendarsAsync(CancellationToken ct = default) => store.ListCalendarsAsync(ct);

    public async Task<OperationResult<CalendarDto>> SaveCalendarAsync(int? calendarId, SaveCalendarRequest request, CancellationToken ct = default)
    {
        if (!await CanOrganisationWideAsync(Permissions.ConfigCalendars, ct))
            return Fail<CalendarDto>(ErrorKind.Forbidden, "forbidden", "Only people who manage calendars for the whole organisation can change calendars.");

        var all = await store.ListCalendarsAsync(ct);
        var existing = calendarId is { } id ? all.FirstOrDefault(c => c.CalendarId == id) : null;
        if (calendarId is not null && existing is null) return Fail<CalendarDto>(ErrorKind.NotFound, "not_found", "This calendar does not exist.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100) return Fail<CalendarDto>(ErrorKind.Validation, "invalid_name", "Enter a calendar name of up to 100 characters.");
        if (await store.CalendarNameExistsAsync(name, calendarId, ct))
            return Fail<CalendarDto>(ErrorKind.Conflict, "calendar_exists", $"There is already a calendar called {name}.");

        if (ToIanaZone(request.TimeZoneId) is not { } zone)
            return Fail<CalendarDto>(ErrorKind.Validation, "invalid_time_zone", "Choose a time zone from the list, for example Asia/Kolkata.");

        var hours = request.Is24x7 ? [] : (request.Hours ?? []);
        if (!request.Is24x7)
        {
            if (hours.Count == 0) return Fail<CalendarDto>(ErrorKind.Validation, "no_hours", "Add the working hours, or mark the calendar as open 24x7.");
            if (hours.Any(h => h.DayOfWeek is < 0 or > 6 || h.StartMinute < 0 || h.EndMinute > 1440 || h.StartMinute >= h.EndMinute))
                return Fail<CalendarDto>(ErrorKind.Validation, "invalid_hours", "Each working period must end after it starts, within the same day.");
            var overlaps = hours.GroupBy(h => h.DayOfWeek).Any(day =>
                day.OrderBy(h => h.StartMinute).Zip(day.OrderBy(h => h.StartMinute).Skip(1)).Any(p => p.Second.StartMinute < p.First.EndMinute));
            if (overlaps) return Fail<CalendarDto>(ErrorKind.Validation, "overlapping_hours", "Working periods on the same day must not overlap.");
        }

        var holidayListIds = (request.HolidayListIds ?? []).Distinct().ToList();
        var lists = await store.ListHolidayListsAsync(ct);
        if (holidayListIds.Any(l => lists.All(x => x.HolidayListId != l)))
            return Fail<CalendarDto>(ErrorKind.Validation, "invalid_holiday_list", "One of the holiday lists no longer exists.");

        // There must always be exactly one active default calendar: it is what units without their own calendar use.
        if (existing is { IsDefault: true } && !request.IsDefault)
            return Fail<CalendarDto>(ErrorKind.Validation, "default_required", "Make another calendar the default instead; one calendar must always be the default.");
        if (request.IsDefault && !request.IsActive)
            return Fail<CalendarDto>(ErrorKind.Validation, "default_inactive", "The default calendar must be active.");
        if (existing is { IsActive: true, UnitsUsing: > 0 } && !request.IsActive)
            return Fail<CalendarDto>(ErrorKind.Validation, "calendar_in_use",
                $"{existing.UnitsUsing} unit(s) use this calendar. Move them to another calendar before turning it off.");
        if (calendarId is null && all.All(c => !c.IsDefault) && !request.IsDefault)
            return Fail<CalendarDto>(ErrorKind.Validation, "default_required", "Make this calendar the default; the organisation has none yet.");

        var savedId = await store.SaveCalendarAsync(calendarId,
            new CalendarValues(name, zone, request.Is24x7, request.IsDefault, request.IsActive, hours, holidayListIds), ct);
        await audit.WriteAsync(currentUser.UserId, calendarId is null ? "calendar.created" : "calendar.updated", "Calendar", savedId.ToString(), request, currentUser.IpAddress, ct);
        return OperationResult<CalendarDto>.Ok((await store.ListCalendarsAsync(ct)).First(c => c.CalendarId == savedId));
    }

    public Task<IReadOnlyList<HolidayListDto>> ListHolidayListsAsync(CancellationToken ct = default) => store.ListHolidayListsAsync(ct);

    public async Task<OperationResult<HolidayListDto>> SaveHolidayListAsync(int? holidayListId, SaveHolidayListRequest request, CancellationToken ct = default)
    {
        if (!await CanOrganisationWideAsync(Permissions.ConfigCalendars, ct))
            return Fail<HolidayListDto>(ErrorKind.Forbidden, "forbidden", "Only people who manage calendars for the whole organisation can change holidays.");

        var lists = await store.ListHolidayListsAsync(ct);
        if (holidayListId is { } id && lists.All(l => l.HolidayListId != id))
            return Fail<HolidayListDto>(ErrorKind.NotFound, "not_found", "This holiday list does not exist.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100) return Fail<HolidayListDto>(ErrorKind.Validation, "invalid_name", "Enter a list name of up to 100 characters.");
        if (await store.HolidayListNameExistsAsync(name, holidayListId, ct))
            return Fail<HolidayListDto>(ErrorKind.Conflict, "list_exists", $"There is already a holiday list called {name}.");

        var holidays = new List<HolidayValues>();
        foreach (var h in request.Holidays ?? [])
        {
            if (!DateTime.TryParseExact(h.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return Fail<HolidayListDto>(ErrorKind.Validation, "invalid_date", $"\"{h.Date}\" is not a date. Use the date picker.");
            var holidayName = h.Name?.Trim() ?? string.Empty;
            if (holidayName.Length is 0 or > 100)
                return Fail<HolidayListDto>(ErrorKind.Validation, "invalid_holiday_name", $"Give the holiday on {h.Date} a name of up to 100 characters.");
            holidays.Add(new HolidayValues(date, holidayName));
        }
        if (holidays.Count > 500) return Fail<HolidayListDto>(ErrorKind.Validation, "too_many", "A list can hold up to 500 holidays.");
        if (holidays.GroupBy(h => h.Date).FirstOrDefault(g => g.Count() > 1) is { } dup)
            return Fail<HolidayListDto>(ErrorKind.Validation, "duplicate_date", $"{dup.Key:yyyy-MM-dd} is listed twice.");

        var savedId = await store.SaveHolidayListAsync(holidayListId, name, holidays.OrderBy(h => h.Date).ToList(), ct);
        await audit.WriteAsync(currentUser.UserId, holidayListId is null ? "holidays.created" : "holidays.updated", "HolidayList", savedId.ToString(), request, currentUser.IpAddress, ct);
        return OperationResult<HolidayListDto>.Ok((await store.ListHolidayListsAsync(ct)).First(l => l.HolidayListId == savedId));
    }

    // ---- SLA policies ----

    public Task<IReadOnlyList<SlaPolicyDto>> ListPoliciesAsync(CancellationToken ct = default) => store.ListPoliciesAsync(ct);

    public async Task<OperationResult<SlaPolicyDto>> SavePolicyAsync(int? slaPolicyId, SaveSlaPolicyRequest request, CancellationToken ct = default)
    {
        var all = await store.ListPoliciesAsync(ct);
        var existing = slaPolicyId is { } id ? all.FirstOrDefault(p => p.SlaPolicyId == id) : null;
        if (slaPolicyId is not null && existing is null) return Fail<SlaPolicyDto>(ErrorKind.NotFound, "not_found", "This SLA policy does not exist.");

        if ((existing is not null && !await CanEditPolicyScopeAsync(existing.ServiceDeskId, ct)) || !await CanEditPolicyScopeAsync(request.ServiceDeskId, ct))
            return Fail<SlaPolicyDto>(ErrorKind.Forbidden, "forbidden",
                "You cannot change this policy. Organisation-wide policies need SLA rights on the organisation; desk policies need them on the desk's team.");

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100) return Fail<SlaPolicyDto>(ErrorKind.Validation, "invalid_name", "Enter a policy name of up to 100 characters.");

        if (request.ServiceDeskId is { } deskId && await desks.GetAsync(deskId, ct) is null)
            return Fail<SlaPolicyDto>(ErrorKind.Validation, "invalid_desk", "Choose a service desk, or leave it empty for every desk.");
        var lookups = await desks.GetLookupsAsync(ct);
        if (request.TicketTypeId is { } typeId && lookups.TicketTypes.All(t => t.Id != typeId))
            return Fail<SlaPolicyDto>(ErrorKind.Validation, "invalid_type", "Choose a ticket type, or leave it empty for every type.");
        if (lookups.Priorities.All(p => p.Id != request.PriorityId))
            return Fail<SlaPolicyDto>(ErrorKind.Validation, "invalid_priority", "Choose a priority.");

        int[] targets = [request.FirstResponseMinutes, request.NextResponseMinutes, request.ResolutionMinutes];
        if (targets.Any(m => m is <= 0 or > MaxTargetMinutes))
            return Fail<SlaPolicyDto>(ErrorKind.Validation, "invalid_target", "Every target must be more than zero minutes.");
        if (request.FirstResponseMinutes > request.ResolutionMinutes)
            return Fail<SlaPolicyDto>(ErrorKind.Validation, "invalid_target", "The first response target cannot be longer than the resolution target.");
        if (request.ReminderMinutes < 0 || request.ReminderMinutes >= targets.Min())
            return Fail<SlaPolicyDto>(ErrorKind.Validation, "invalid_reminder", "The reminder must come before the shortest target (or be 0 for no reminder).");

        var levels = (request.Levels ?? []).OrderBy(l => l.Level).ToList();
        var escalates = request.EscalateOnFirstResponse || request.EscalateOnNextResponse || request.EscalateOnResolution;
        if (escalates && levels.Count == 0)
            return Fail<SlaPolicyDto>(ErrorKind.Validation, "no_levels", "Add at least one escalation level, or turn escalation off.");
        if (levels.Count > MaxLevels) return Fail<SlaPolicyDto>(ErrorKind.Validation, "too_many_levels", $"Use up to {MaxLevels} escalation levels.");
        var roleList = await roles.ListAsync(ct);
        for (var i = 0; i < levels.Count; i++)
        {
            var l = levels[i];
            if (l.Level != i + 1) return Fail<SlaPolicyDto>(ErrorKind.Validation, "invalid_levels", "Escalation levels must be numbered 1, 2, 3 in order.");
            if (roleList.All(r => r.RoleId != l.RoleId)) return Fail<SlaPolicyDto>(ErrorKind.Validation, "invalid_role", $"Choose who handles level {l.Level}.");
            var last = i == levels.Count - 1;
            if (!last && l.WindowMinutes is not > 0 or > MaxTargetMinutes)
                return Fail<SlaPolicyDto>(ErrorKind.Validation, "invalid_window", $"Say how long level {l.Level} has before it moves to level {l.Level + 1}.");
        }
        // The last level has no window: it stays there and gets reminders until the ticket is handled.
        levels = levels.Select((l, i) => l with { WindowMinutes = i == levels.Count - 1 ? null : l.WindowMinutes }).ToList();

        if (request.IsActive && await store.ActiveScopeTakenByAsync(request.ServiceDeskId, request.TicketTypeId, request.PriorityId, slaPolicyId, ct) is { } other)
            return Fail<SlaPolicyDto>(ErrorKind.Conflict, "policy_overlap",
                $"\"{other}\" already covers this desk, type and priority. Turn it off or change it first.");

        var savedId = await store.SavePolicyAsync(slaPolicyId, new SlaPolicyValues(name, request.ServiceDeskId, request.TicketTypeId, request.PriorityId,
            request.FirstResponseMinutes, request.NextResponseMinutes, request.ResolutionMinutes, request.ReminderMinutes,
            request.EscalateOnFirstResponse, request.EscalateOnNextResponse, request.EscalateOnResolution, request.IsActive, levels), ct);
        await audit.WriteAsync(currentUser.UserId, slaPolicyId is null ? "sla.created" : "sla.updated", "SlaPolicy", savedId.ToString(), request, currentUser.IpAddress, ct);
        return OperationResult<SlaPolicyDto>.Ok((await store.ListPoliciesAsync(ct)).First(p => p.SlaPolicyId == savedId));
    }

    // ---- Per-unit settings ----

    public async Task<IReadOnlyList<UnitSettingsDto>> ListUnitSettingsAsync(CancellationToken ct = default)
    {
        var actor = RequireActor();
        var units = await store.ListUnitSettingsAsync(ct);
        var calendars = await store.ListCalendarsAsync(ct);
        var result = new List<UnitSettingsDto>();
        foreach (var u in units)
        {
            var canCalendar = await permissions.CanAsync(actor, Permissions.ConfigCalendars, u.OrgUnitId, ct);
            var canRouting = (OrgUnitType)u.UnitTypeValue == OrgUnitType.Team && await permissions.CanAsync(actor, Permissions.ConfigRouting, u.OrgUnitId, ct);
            if (!canCalendar && !canRouting) continue;
            result.Add(ToDto(u, units, calendars, canCalendar, canRouting));
        }
        return result;
    }

    public async Task<OperationResult<UnitSettingsDto>> SaveUnitSettingsAsync(int orgUnitId, SaveUnitSettingsRequest request, CancellationToken ct = default)
    {
        var actor = RequireActor();
        var units = await store.ListUnitSettingsAsync(ct);
        var unit = units.FirstOrDefault(u => u.OrgUnitId == orgUnitId);
        if (unit is null) return Fail<UnitSettingsDto>(ErrorKind.NotFound, "not_found", "This unit does not exist.");

        var isTeam = (OrgUnitType)unit.UnitTypeValue == OrgUnitType.Team;
        var canCalendar = await permissions.CanAsync(actor, Permissions.ConfigCalendars, orgUnitId, ct);
        var canRouting = isTeam && await permissions.CanAsync(actor, Permissions.ConfigRouting, orgUnitId, ct);
        if (!canCalendar && !canRouting) return Fail<UnitSettingsDto>(ErrorKind.Forbidden, "forbidden", "You cannot change settings for this unit.");

        var calendarId = unit.CalendarId;
        if (request.CalendarId != unit.CalendarId)
        {
            if (!canCalendar) return Fail<UnitSettingsDto>(ErrorKind.Forbidden, "forbidden", "You cannot change the business hours of this unit.");
            var calendars = await store.ListCalendarsAsync(ct);
            if (request.CalendarId is { } cid && calendars.FirstOrDefault(c => c.CalendarId == cid) is not { IsActive: true })
                return Fail<UnitSettingsDto>(ErrorKind.Validation, "invalid_calendar", "Choose an active calendar.");
            calendarId = request.CalendarId;
        }

        (byte, int, int)? assignment = null;
        var current = (unit.AssignmentMode, unit.MaxOpenTickets, unit.UnassignedAlertMinutes);
        if (isTeam && request.AssignmentMode is not null)
        {
            if (!Enum.TryParse<AssignmentMode>(request.AssignmentMode, true, out var mode) || !Enum.IsDefined(mode))
                return Fail<UnitSettingsDto>(ErrorKind.Validation, "invalid_mode", "Choose Manual, Load balanced or Round robin.");
            if (request.MaxOpenTickets is not (>= 1 and <= 500))
                return Fail<UnitSettingsDto>(ErrorKind.Validation, "invalid_max_open", "The ticket limit per agent must be between 1 and 500.");
            if (request.UnassignedAlertMinutes is not (>= 1 and <= 1440))
                return Fail<UnitSettingsDto>(ErrorKind.Validation, "invalid_alert", "The unassigned alert must be between 1 and 1,440 minutes.");
            var wanted = ((byte?)mode, request.MaxOpenTickets, request.UnassignedAlertMinutes);
            if (wanted != current)
            {
                if (!canRouting) return Fail<UnitSettingsDto>(ErrorKind.Forbidden, "forbidden", "You cannot change how this team's tickets are assigned.");
                assignment = ((byte)mode, request.MaxOpenTickets.Value, request.UnassignedAlertMinutes.Value);
            }
        }

        await store.SaveUnitSettingsAsync(orgUnitId, calendarId, assignment, ct);
        await audit.WriteAsync(actor, "unit.settings.updated", "OrgUnit", orgUnitId.ToString(), request, currentUser.IpAddress, ct);

        var after = await store.ListUnitSettingsAsync(ct);
        return OperationResult<UnitSettingsDto>.Ok(ToDto(after.First(u => u.OrgUnitId == orgUnitId), after, await store.ListCalendarsAsync(ct), canCalendar, canRouting));
    }

    // ---- helpers ----

    private static UnitSettingsDto ToDto(UnitSettingsRow u, IReadOnlyList<UnitSettingsRow> all, IReadOnlyList<CalendarDto> calendars, bool canCalendar, bool canRouting)
    {
        // Same rule as the engine: the nearest unit with an active calendar, else the default calendar.
        var byId = all.ToDictionary(x => x.OrgUnitId);
        string? effective = null;
        var inherited = false;
        for (var cursor = u; cursor is not null; cursor = cursor.ParentId is { } p ? byId.GetValueOrDefault(p) : null)
        {
            if (cursor.CalendarId is { } cid && calendars.FirstOrDefault(c => c.CalendarId == cid) is { IsActive: true } cal)
            {
                effective = cal.Name;
                inherited = cursor.OrgUnitId != u.OrgUnitId;
                break;
            }
        }
        if (effective is null)
        {
            effective = calendars.FirstOrDefault(c => c.IsDefault)?.Name;
            inherited = true;
        }

        var isTeam = (OrgUnitType)u.UnitTypeValue == OrgUnitType.Team;
        // Teams without saved settings use the engine's defaults.
        return new UnitSettingsDto(u.OrgUnitId, u.ParentId, ((OrgUnitType)u.UnitTypeValue).ToString(), u.Name, u.Code, u.IsActive,
            u.CalendarId, effective, inherited,
            isTeam ? ((AssignmentMode)(u.AssignmentMode ?? (byte)AssignmentMode.LoadBalanced)).ToString() : null,
            isTeam ? u.MaxOpenTickets ?? 20 : null,
            isTeam ? u.UnassignedAlertMinutes ?? 15 : null,
            canCalendar, canRouting);
    }

    private async Task<bool> CanEditPolicyScopeAsync(int? serviceDeskId, CancellationToken ct)
    {
        if (serviceDeskId is null) return await CanOrganisationWideAsync(Permissions.ConfigSla, ct);
        var desk = await desks.GetAsync(serviceDeskId.Value, ct);
        return desk is not null && await permissions.CanAsync(RequireActor(), Permissions.ConfigSla, desk.DefaultTeamId, ct);
    }

    /// <summary>Held on the organisation unit itself, which then covers every unit below it.</summary>
    private async Task<bool> CanOrganisationWideAsync(string permission, CancellationToken ct)
    {
        var actor = RequireActor();
        foreach (var root in (await store.ListUnitSettingsAsync(ct)).Where(u => (OrgUnitType)u.UnitTypeValue == OrgUnitType.Organization))
            if (await permissions.CanAsync(actor, permission, root.OrgUnitId, ct)) return true;
        return false;
    }

    /// <summary>Stores IANA ids so calendars behave the same on Windows and Linux hosts.</summary>
    private static string? ToIanaZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone)) return null;
        if (zone.HasIanaId) return zone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : null;
    }

    private long RequireActor() => currentUser.UserId ?? throw new InvalidOperationException("No signed-in user.");

    private static OperationResult<T> Fail<T>(ErrorKind kind, string code, string message) => OperationResult<T>.Fail(kind, code, message);
}
