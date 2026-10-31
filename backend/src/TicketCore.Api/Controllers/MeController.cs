using Microsoft.AspNetCore.Mvc;
using TicketCore.Core;
using TicketCore.Core.Access;
using TicketCore.Api.Infrastructure;
using TicketCore.Data.Users;
using TicketCore.Data.Users.Queries;
using TicketCore.ViewModels.Access;

namespace TicketCore.Api.Controllers;

[ApiController]
[Route("api/v1/me")]
public sealed class MeController(ICurrentUser currentUser, IUserQueries users, IPermissionEvaluator evaluator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken ct)
    {
        var user = await users.GetByIdAsync(currentUser.UserId!.Value, ct);
        if (user is null) return Unauthorized();
        return Ok(new MeResponse(user.UserId, user.UserName, user.Email, user.DisplayName, user.UserType.ToString(), user.HomeOrgUnitId));
    }

    /// <summary>Check-in state. Agents who are Available receive automatically assigned tickets.</summary>
    [HttpGet("availability")]
    [RequirePermission(Common.Permissions.AgentCheckIn)]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailability([FromServices] IAvailabilityStore store, CancellationToken ct)
    {
        var row = await store.GetAsync(currentUser.UserId!.Value, ct);
        return Ok(new AvailabilityResponse(row is null ? nameof(AgentStatus.Offline) : ((AgentStatus)row.Status).ToString(), row?.ChangedAt));
    }

    [HttpPut("availability")]
    [RequirePermission(Common.Permissions.AgentCheckIn)]
    public async Task<ActionResult<AvailabilityResponse>> SetAvailability(SetAvailabilityRequest request,
        [FromServices] IAvailabilityStore store, [FromServices] TimeProvider clock, CancellationToken ct)
    {
        if (!Enum.TryParse<AgentStatus>(request.Status, true, out var status))
            return this.ToProblem(new Common.OperationError(Common.ErrorKind.Validation, "invalid_status", "Choose Available, Busy, Away or Offline."));
        var now = clock.GetUtcNow().UtcDateTime;
        await store.SetAsync(currentUser.UserId!.Value, status, now, automatic: false, ct);
        return Ok(new AvailabilityResponse(status.ToString(), now));
    }

    [HttpGet("permissions")]
    public async Task<ActionResult<MyPermissionsResponse>> Permissions(CancellationToken ct)
    {
        var access = await evaluator.GetAccessAsync(currentUser.UserId!.Value, ct);
        return Ok(new MyPermissionsResponse(access.Grants));
    }
}
