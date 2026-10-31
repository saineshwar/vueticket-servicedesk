using Microsoft.AspNetCore.Mvc;
using TicketCore.Api.Infrastructure;
using TicketCore.Common;
using TicketCore.Core.Access;
using TicketCore.Data.Access.Queries;
using TicketCore.ViewModels.Access;

namespace TicketCore.Api.Controllers;

[ApiController]
[Route("api/v1/roles")]
[RequirePermission(Permissions.RoleManage)]
public sealed class RolesController(IRoleQueries queries, IRoleService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken ct) => Ok(await queries.ListAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RoleDto>> Get(int id, CancellationToken ct) =>
        await queries.GetByIdAsync(id, ct) is { } role ? Ok(role) : NotFound();

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create(SaveRoleRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.CreateAsync(request, ct));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<RoleDto>> Update(int id, SaveRoleRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct) =>
        this.ToActionResult(await service.DeleteAsync(id, ct));
}

[ApiController]
[Route("api/v1/permissions")]
[RequirePermission(Permissions.RoleManage)]
public sealed class PermissionsController : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<PermissionDto>> Catalog() =>
        Ok(Permissions.Catalog.Select(p => new PermissionDto(p.Key, p.Group, p.Description)));
}

[ApiController]
[Route("api/v1/role-assignments")]
[RequirePermission(Permissions.RoleManage)]
public sealed class RoleAssignmentsController(IRoleAssignmentQueries queries, IRoleService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleAssignmentDto>>> List([FromQuery] long? userId, [FromQuery] int? orgUnitId, CancellationToken ct) =>
        Ok(await queries.ListAsync(userId, orgUnitId, ct));

    [HttpPost]
    public async Task<ActionResult<RoleAssignmentDto>> Create(CreateRoleAssignmentRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.AssignAsync(request, ct));

    [HttpDelete("{id:long}")]
    public async Task<ActionResult> Delete(long id, CancellationToken ct) =>
        this.ToActionResult(await service.UnassignAsync(id, ct));
}
