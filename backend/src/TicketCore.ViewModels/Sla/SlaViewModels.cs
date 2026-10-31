namespace TicketCore.ViewModels.Sla;

// ---- On tickets (support staff only) ----

/// <summary>One SLA target. Clock is first_response, next_response or resolution.</summary>
public sealed record SlaClockDto(string Clock, DateTime? DueAt, DateTime? MetAt, DateTime? BreachedAt);

/// <summary>While paused (awaiting the requester) the due times move out by the paused time once work resumes.</summary>
public sealed record TicketSlaDto(string? PolicyName, IReadOnlyList<SlaClockDto> Clocks, DateTime? PausedAt);

public sealed record TicketEscalationDto(
    int Level,
    string? RoleName,
    IReadOnlyList<string> Reasons,
    long? OwnerUserId,
    string? OwnerName,
    DateTime StartedAt,
    DateTime? WindowEndsAt);

// ---- Business calendars ----

/// <summary>DayOfWeek 0 = Sunday. Minutes from midnight in the calendar's time zone.</summary>
public sealed record CalendarHoursDto(int DayOfWeek, int StartMinute, int EndMinute);

public sealed record CalendarDto(
    int CalendarId,
    string Name,
    string TimeZoneId,
    bool Is24x7,
    bool IsDefault,
    bool IsActive,
    int UnitsUsing,
    IReadOnlyList<CalendarHoursDto> Hours,
    IReadOnlyList<int> HolidayListIds);

public sealed record SaveCalendarRequest(
    string Name,
    string TimeZoneId,
    bool Is24x7,
    bool IsDefault,
    bool IsActive,
    IReadOnlyList<CalendarHoursDto>? Hours,
    IReadOnlyList<int>? HolidayListIds);

/// <summary>Date is yyyy-MM-dd.</summary>
public sealed record HolidayDto(string Date, string Name);

public sealed record HolidayListDto(int HolidayListId, string Name, IReadOnlyList<HolidayDto> Holidays);

public sealed record SaveHolidayListRequest(string Name, IReadOnlyList<HolidayDto>? Holidays);

// ---- SLA policies ----

/// <summary>WindowMinutes is the business time this level has before the next level takes over; null on the last level.</summary>
public sealed record EscalationLevelDto(int Level, int RoleId, string? RoleName, int? WindowMinutes);

public sealed record SlaPolicyDto(
    int SlaPolicyId,
    string Name,
    int? ServiceDeskId,
    string? ServiceDeskName,
    int? TicketTypeId,
    string? TicketTypeName,
    int PriorityId,
    string PriorityName,
    int FirstResponseMinutes,
    int NextResponseMinutes,
    int ResolutionMinutes,
    int ReminderMinutes,
    bool EscalateOnFirstResponse,
    bool EscalateOnNextResponse,
    bool EscalateOnResolution,
    bool IsActive,
    IReadOnlyList<EscalationLevelDto> Levels);

public sealed record SaveSlaPolicyRequest(
    string Name,
    int? ServiceDeskId,
    int? TicketTypeId,
    int PriorityId,
    int FirstResponseMinutes,
    int NextResponseMinutes,
    int ResolutionMinutes,
    int ReminderMinutes,
    bool EscalateOnFirstResponse,
    bool EscalateOnNextResponse,
    bool EscalateOnResolution,
    bool IsActive,
    IReadOnlyList<EscalationLevelDto>? Levels);

// ---- Per-unit settings: business hours for any unit, assignment for teams ----

public sealed record UnitSettingsDto(
    int OrgUnitId,
    int? ParentId,
    string UnitType,
    string Name,
    string Code,
    bool IsActive,
    int? CalendarId,
    string? EffectiveCalendarName,
    bool CalendarInherited,
    string? AssignmentMode,
    int? MaxOpenTickets,
    int? UnassignedAlertMinutes,
    bool CanEditCalendar,
    bool CanEditAssignment);

/// <summary>A null CalendarId means "same as the parent unit". Assignment fields apply to teams only.</summary>
public sealed record SaveUnitSettingsRequest(int? CalendarId, string? AssignmentMode, int? MaxOpenTickets, int? UnassignedAlertMinutes);

/// <summary>Choices for the SLA screens, so they need no other admin permission.</summary>
public sealed record SlaOptionsResponse(
    IReadOnlyList<TicketCore.ViewModels.ServiceDesks.LookupItem> Desks,
    IReadOnlyList<TicketCore.ViewModels.ServiceDesks.LookupItem> TicketTypes,
    IReadOnlyList<TicketCore.ViewModels.ServiceDesks.LookupItem> Priorities,
    IReadOnlyList<TicketCore.ViewModels.ServiceDesks.LookupItem> Roles,
    IReadOnlyList<TicketCore.ViewModels.ServiceDesks.LookupItem> Calendars);
