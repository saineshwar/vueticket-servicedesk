using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TicketCore.Api.Infrastructure;
using TicketCore.Core.Setup;
using TicketCore.ViewModels.Auth;

namespace TicketCore.Api.Controllers;

/// <summary>First-run setup wizard. Closes itself once the first administrator exists.</summary>
[ApiController]
[Route("api/v1/setup")]
[AllowAnonymous]
public sealed class SetupController(ISetupService setup) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<SetupStatusResponse>> Status(CancellationToken ct) =>
        Ok(new SetupStatusResponse(await setup.IsInitializedAsync(ct)));

    [HttpPost]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult> Initialize(SetupRequest request, CancellationToken ct) =>
        this.ToActionResult(await setup.InitializeAsync(request, ct));
}
