using Microsoft.AspNetCore.Mvc;
using TicketCore.Api.Infrastructure;
using TicketCore.Common;
using TicketCore.Core.Sla;
using TicketCore.ViewModels.Sla;

namespace TicketCore.Api.Controllers;

/// <summary>Business calendars, holidays, SLA policies and per-unit settings. Scope checks happen in SlaConfigService.</summary>
[ApiController]
[Route("api/v1/admin")]
public sealed class SlaConfigController(ISlaConfigService service) : ControllerBase
{
    [HttpGet("sla/options")]
    [RequirePermission(Permissions.ConfigSla, Permissions.ConfigCalendars, Permissions.ConfigRouting)]
    public async Task<ActionResult<SlaOptionsResponse>> Options(CancellationToken ct) => Ok(await service.GetOptionsAsync(ct));

    [HttpGet("calendars")]
    [RequirePermission(Permissions.ConfigCalendars)]
    public async Task<ActionResult<IReadOnlyList<CalendarDto>>> Calendars(CancellationToken ct) => Ok(await service.ListCalendarsAsync(ct));

    [HttpPost("calendars")]
    [RequirePermission(Permissions.ConfigCalendars)]
    public async Task<ActionResult<CalendarDto>> CreateCalendar(SaveCalendarRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.SaveCalendarAsync(null, request, ct));

    [HttpPut("calendars/{id:int}")]
    [RequirePermission(Permissions.ConfigCalendars)]
    public async Task<ActionResult<CalendarDto>> UpdateCalendar(int id, SaveCalendarRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.SaveCalendarAsync(id, request, ct));

    [HttpGet("holiday-lists")]
    [RequirePermission(Permissions.ConfigCalendars)]
    public async Task<ActionResult<IReadOnlyList<HolidayListDto>>> HolidayLists(CancellationToken ct) => Ok(await service.ListHolidayListsAsync(ct));

    [HttpPost("holiday-lists")]
    [RequirePermission(Permissions.ConfigCalendars)]
    public async Task<ActionResult<HolidayListDto>> CreateHolidayList(SaveHolidayListRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.SaveHolidayListAsync(null, request, ct));

    [HttpPut("holiday-lists/{id:int}")]
    [RequirePermission(Permissions.ConfigCalendars)]
    public async Task<ActionResult<HolidayListDto>> UpdateHolidayList(int id, SaveHolidayListRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.SaveHolidayListAsync(id, request, ct));

    [HttpGet("sla-policies")]
    [RequirePermission(Permissions.ConfigSla)]
    public async Task<ActionResult<IReadOnlyList<SlaPolicyDto>>> Policies(CancellationToken ct) => Ok(await service.ListPoliciesAsync(ct));

    [HttpPost("sla-policies")]
    [RequirePermission(Permissions.ConfigSla)]
    public async Task<ActionResult<SlaPolicyDto>> CreatePolicy(SaveSlaPolicyRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.SavePolicyAsync(null, request, ct));

    [HttpPut("sla-policies/{id:int}")]
    [RequirePermission(Permissions.ConfigSla)]
    public async Task<ActionResult<SlaPolicyDto>> UpdatePolicy(int id, SaveSlaPolicyRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.SavePolicyAsync(id, request, ct));

    [HttpGet("unit-settings")]
    [RequirePermission(Permissions.ConfigCalendars, Permissions.ConfigRouting)]
    public async Task<ActionResult<IReadOnlyList<UnitSettingsDto>>> UnitSettings(CancellationToken ct) => Ok(await service.ListUnitSettingsAsync(ct));

    [HttpPut("unit-settings/{id:int}")]
    [RequirePermission(Permissions.ConfigCalendars, Permissions.ConfigRouting)]
    public async Task<ActionResult<UnitSettingsDto>> SaveUnitSettings(int id, SaveUnitSettingsRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.SaveUnitSettingsAsync(id, request, ct));
}
