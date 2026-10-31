using Microsoft.AspNetCore.Mvc;
using TicketCore.Api.Infrastructure;
using TicketCore.Common;
using TicketCore.Core.Users;
using TicketCore.Data.Users.Queries;
using TicketCore.ViewModels.Users;

namespace TicketCore.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(IUserQueries queries, IUserService service) : ControllerBase
{
    /// <summary>
    /// Search users. Role managers use it to pick who gets a role; agents use it to pick the requester
    /// when raising a ticket on someone's behalf.
    /// </summary>
    [HttpGet]
    [RequirePermission(Permissions.UserManage, Permissions.RoleManage, Permissions.TicketCreateOnBehalf, Permissions.RequesterManage)]
    public async Task<ActionResult<PagedResult<UserListItemDto>>> Search(
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await queries.SearchAsync(q, page, pageSize, ct));

    [HttpPost]
    [RequirePermission(Permissions.UserManage)]
    public async Task<ActionResult<UserListItemDto>> Create(CreateUserRequest request, CancellationToken ct) =>
        this.ToActionResult(await service.CreateAsync(request, ct));
}
