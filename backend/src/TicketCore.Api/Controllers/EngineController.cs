using Microsoft.AspNetCore.Mvc;
using TicketCore.Api.Infrastructure;
using TicketCore.Common;
using TicketCore.Core.Engine;
using TicketCore.ViewModels.Engine;
using TicketCore.ViewModels.Users;

namespace TicketCore.Api.Controllers;

/// <summary>When the engine worker's tasks last ran, their health, and the history of runs that did work or failed.</summary>
[ApiController]
[Route("api/v1/admin/engine")]
[RequirePermission(Permissions.ConfigJobs)]
public sealed class EngineController(IEngineMonitorService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EngineOverviewResponse>> Overview(CancellationToken ct) => Ok(await service.GetOverviewAsync(ct));

    [HttpGet("runs")]
    public async Task<ActionResult<PagedResult<EngineRunDto>>> Runs(
        [FromQuery] string? task = null, [FromQuery] bool errorsOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        Ok(await service.GetRunsAsync(task, errorsOnly, page, pageSize, ct));
}
