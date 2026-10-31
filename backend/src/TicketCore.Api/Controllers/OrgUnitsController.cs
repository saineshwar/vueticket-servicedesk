using Microsoft.AspNetCore.Mvc;
using TicketCore.Api.Infrastructure;
using TicketCore.Common;
using TicketCore.Core.Organization;
using TicketCore.Data.Organization.Queries;
using TicketCore.ViewModels.Organization;

namespace TicketCore.Api.Controllers;

[ApiController]
[Route("api/v1/org-units")]
public sealed class OrgUnitsController(IOrgUnitQueries queries, IOrgUnitService service) : ControllerBase
{
    /// <summary>The whole tree as a flat list (parentId links). Any signed-in user can read the structure.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrgUnitDto>>> GetTree(CancellationToken ct) => Ok(await queries.GetTreeAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrgUnitDto>> Get(int id, CancellationToken ct) =>
        await queries.GetByIdAsync(id, ct) is { } unit ? Ok(unit) : NotFound();

    [HttpPost]
    [RequirePermission(Permissions.OrgManage)]
    public async Task<ActionResult<OrgUnitDto>> Create(CreateOrgUnitRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:int}")]
    [RequirePermission(Permissions.OrgManage)]
    public async Task<ActionResult<OrgUnitDto>> Update(int id, UpdateOrgUnitRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.UpdateAsync(id, request, ct));

    [HttpPost("{id:int}/move")]
    [RequirePermission(Permissions.OrgManage)]
    public async Task<ActionResult<OrgUnitDto>> Move(int id, MoveOrgUnitRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.MoveAsync(id, request, ct));
}
