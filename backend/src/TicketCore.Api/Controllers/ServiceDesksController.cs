using Microsoft.AspNetCore.Mvc;
using TicketCore.Api.Infrastructure;
using TicketCore.Common;
using TicketCore.Core;
using TicketCore.Core.ServiceDesks;
using TicketCore.Core.Tickets;
using TicketCore.Data.ServiceDesks.Queries;
using TicketCore.Data.Users.Queries;
using TicketCore.ViewModels.ServiceDesks;

namespace TicketCore.Api.Controllers;

/// <summary>Desks the signed-in user can raise requests on (active only, filtered by audience).</summary>
[ApiController]
[Route("api/v1/service-desks")]
public sealed class ServiceDesksController(IServiceDeskQueries queries, IUserQueries users, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServiceDeskDto>>> Available(CancellationToken ct)
    {
        var me = await users.GetByIdAsync(currentUser.UserId!.Value, ct);
        if (me is null) return Unauthorized();
        var desks = await queries.ListAsync(activeOnly: true, ct);
        return Ok(desks.Where(d => TicketRules.AudienceAllows(Enum.Parse<DeskAudience>(d.Audience), me.UserType) && d.Categories.Count > 0));
    }
}

[ApiController]
[Route("api/v1/lookups")]
public sealed class LookupsController(IServiceDeskQueries queries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LookupsResponse>> Get(CancellationToken ct) => Ok(await queries.GetLookupsAsync(ct));
}

/// <summary>Desk configuration. Unit-level checks (config.desks on the desk's team) happen in ServiceDeskService.</summary>
[ApiController]
[Route("api/v1/admin/service-desks")]
[RequirePermission(Permissions.ConfigDesks)]
public sealed class AdminServiceDesksController(IServiceDeskQueries queries, IServiceDeskService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServiceDeskDto>>> List(CancellationToken ct) => Ok(await queries.ListAsync(activeOnly: false, ct));

    [HttpPost]
    public async Task<ActionResult<ServiceDeskDto>> Create(SaveServiceDeskRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ServiceDeskDto>> Update(int id, SaveServiceDeskRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.UpdateAsync(id, request, ct));

    [HttpPost("{id:int}/categories")]
    public async Task<ActionResult<ServiceDeskDto>> CreateCategory(int id, SaveCategoryRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.CreateCategoryAsync(id, request, ct));

    [HttpPut("categories/{categoryId:int}")]
    public async Task<ActionResult<ServiceDeskDto>> UpdateCategory(int categoryId, SaveCategoryRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.UpdateCategoryAsync(categoryId, request, ct));
}
