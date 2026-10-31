using Microsoft.AspNetCore.Mvc;
using TicketCore.Api.Infrastructure;
using TicketCore.Core;
using TicketCore.Core.Productivity;
using TicketCore.Data.Messaging;
using TicketCore.ViewModels.Productivity;

namespace TicketCore.Api.Controllers;

[ApiController]
[Route("api/v1/canned-responses")]
public sealed class CannedResponsesController(ICannedResponseService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CannedResponseDto>>> List(CancellationToken ct) => Ok(await service.ListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<CannedResponseDto>> Create(SaveCannedResponseRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CannedResponseDto>> Update(int id, SaveCannedResponseRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct) => this.ToActionResult(await service.DeleteAsync(id, ct));
}

/// <summary>The signed-in user's own notifications.</summary>
[ApiController]
[Route("api/v1/notifications")]
public sealed class NotificationsController(INotificationStore store, ICurrentUser currentUser, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<NotificationsResponse>> List([FromQuery] int take = 20, CancellationToken ct = default) =>
        Ok(await store.ListAsync(currentUser.UserId!.Value, Math.Clamp(take, 1, 100), ct));

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        await store.MarkReadAsync(currentUser.UserId!.Value, id, clock.GetUtcNow().UtcDateTime, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await store.MarkReadAsync(currentUser.UserId!.Value, null, clock.GetUtcNow().UtcDateTime, ct);
        return NoContent();
    }
}
