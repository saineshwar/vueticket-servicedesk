using Microsoft.AspNetCore.Mvc;
using TicketCore.Core.Dashboard;
using TicketCore.ViewModels.Dashboard;

namespace TicketCore.Api.Controllers;

/// <summary>The home page for every signed-in user; the service limits each section to the caller's rights.</summary>
[ApiController]
[Route("api/v1/dashboard")]
public sealed class DashboardController(IDashboardService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken ct) => Ok(await service.GetAsync(ct));
}
