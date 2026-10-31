using TicketCore.Common;
using TicketCore.Data.Access.Commands;
using TicketCore.Data.Access.Queries;
using TicketCore.Data.Audit;
using TicketCore.Data.Organization.Queries;
using TicketCore.Data.Users.Queries;
using TicketCore.Models.Access;
using TicketCore.ViewModels.Access;

namespace TicketCore.Core.Access;

public interface IRoleService
{
    Task<OperationResult<RoleDto>> CreateAsync(SaveRoleRequest request, CancellationToken ct = default);
    Task<OperationResult<RoleDto>> UpdateAsync(int roleId, SaveRoleRequest request, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int roleId, CancellationToken ct = default);
    Task<OperationResult<RoleAssignmentDto>> AssignAsync(CreateRoleAssignmentRequest request, CancellationToken ct = default);
    Task<OperationResult> UnassignAsync(long roleAssignmentId, CancellationToken ct = default);
}

public sealed class RoleService(
    IRoleQueries roleQueries,
    IRoleCommand roleCommand,
    IRoleAssignmentQueries assignmentQueries,
    IRoleAssignmentCommand assignmentCommand,
    IUserQueries userQueries,
    IOrgUnitQueries orgUnitQueries,
    IPermissionEvaluator permissionEvaluator,
    AccessVersion accessVersion,
    IAuditCommand audit,
    ICurrentUser currentUser,
    TimeProvider clock) : IRoleService
{
    public async Task<OperationResult<RoleDto>> CreateAsync(SaveRoleRequest request, CancellationToken ct = default)
    {
        var (name, permissions, error) = Normalize(request);
        if (error is not null) return OperationResult<RoleDto>.Fail(ErrorKind.Validation, "invalid_role", error);

        if (await roleQueries.NameExistsAsync(name, null, ct))
            return OperationResult<RoleDto>.Fail(ErrorKind.Conflict, "name_taken", $"A role named {name} already exists.");

        var id = await roleCommand.CreateAsync(name, request.Description?.Trim(), permissions, ct);
        await audit.WriteAsync(currentUser.UserId, "role.created", "Role", id.ToString(), new { name, permissions }, currentUser.IpAddress, ct);
        return OperationResult<RoleDto>.Ok((await roleQueries.GetByIdAsync(id, ct))!);
    }

    public async Task<OperationResult<RoleDto>> UpdateAsync(int roleId, SaveRoleRequest request, CancellationToken ct = default)
    {
        var existing = await roleQueries.GetByIdAsync(roleId, ct);
        if (existing is null) return OperationResult<RoleDto>.Fail(ErrorKind.NotFound, "not_found", "This role does not exist.");
        if (existing.IsSystem)
            return OperationResult<RoleDto>.Fail(ErrorKind.Validation, "system_role", "The System Admin role is locked. Copy it to make a changed version.");

        var (name, permissions, error) = Normalize(request);
        if (error is not null) return OperationResult<RoleDto>.Fail(ErrorKind.Validation, "invalid_role", error);

        if (await roleQueries.NameExistsAsync(name, roleId, ct))
            return OperationResult<RoleDto>.Fail(ErrorKind.Conflict, "name_taken", $"A role named {name} already exists.");

        await roleCommand.UpdateAsync(roleId, name, request.Description?.Trim(), permissions, ct);
        accessVersion.Bump();
        await audit.WriteAsync(currentUser.UserId, "role.updated", "Role", roleId.ToString(),
            new { before = new { existing.Name, existing.Permissions }, after = new { name, permissions } }, currentUser.IpAddress, ct);
        return OperationResult<RoleDto>.Ok((await roleQueries.GetByIdAsync(roleId, ct))!);
    }

    public async Task<OperationResult> DeleteAsync(int roleId, CancellationToken ct = default)
    {
        var existing = await roleQueries.GetByIdAsync(roleId, ct);
        if (existing is null) return OperationResult.Fail(ErrorKind.NotFound, "not_found", "This role does not exist.");
        if (existing.IsSystem) return OperationResult.Fail(ErrorKind.Validation, "system_role", "The System Admin role cannot be deleted.");
        if (existing.AssignmentCount > 0)
            return OperationResult.Fail(ErrorKind.Conflict, "role_in_use",
                $"{existing.Name} is still assigned {existing.AssignmentCount} {(existing.AssignmentCount == 1 ? "time" : "times")}. Remove those assignments first.");

        await roleCommand.DeleteAsync(roleId, ct);
        await audit.WriteAsync(currentUser.UserId, "role.deleted", "Role", roleId.ToString(), new { existing.Name }, currentUser.IpAddress, ct);
        return OperationResult.Ok();
    }

    public async Task<OperationResult<RoleAssignmentDto>> AssignAsync(CreateRoleAssignmentRequest request, CancellationToken ct = default)
    {
        var user = await userQueries.GetByIdAsync(request.UserId, ct);
        if (user is null) return OperationResult<RoleAssignmentDto>.Fail(ErrorKind.Validation, "user_not_found", "This user does not exist.");
        var role = await roleQueries.GetByIdAsync(request.RoleId, ct);
        if (role is null)
            return OperationResult<RoleAssignmentDto>.Fail(ErrorKind.Validation, "role_not_found", "This role does not exist.");
        if (await orgUnitQueries.GetByIdAsync(request.OrgUnitId, ct) is null)
            return OperationResult<RoleAssignmentDto>.Fail(ErrorKind.Validation, "unit_not_found", "This unit does not exist.");

        // You can only hand out what you hold yourself on that unit, so role managers cannot escalate their own access.
        var actorId = currentUser.UserId
            ?? throw new InvalidOperationException("Role assignment requires a signed-in user.");
        if (!await permissionEvaluator.CanAsync(actorId, Permissions.RoleManage, request.OrgUnitId, ct))
            return OperationResult<RoleAssignmentDto>.Fail(ErrorKind.Forbidden, "forbidden", "You cannot manage roles on this unit.");
        foreach (var key in role.Permissions)
        {
            if (!await permissionEvaluator.CanAsync(actorId, key, request.OrgUnitId, ct))
                return OperationResult<RoleAssignmentDto>.Fail(ErrorKind.Forbidden, "exceeds_own_access",
                    $"{role.Name} includes permissions you do not hold on this unit ({key}), so you cannot give it.");
        }
        if (await assignmentQueries.ExistsAsync(request.UserId, request.RoleId, request.OrgUnitId, ct))
            return OperationResult<RoleAssignmentDto>.Fail(ErrorKind.Conflict, "already_assigned", "This person already has this role on this unit.");

        var id = await assignmentCommand.CreateAsync(new RoleAssignment
        {
            UserId = request.UserId,
            RoleId = request.RoleId,
            OrgUnitId = request.OrgUnitId,
            IncludeDescendants = request.IncludeDescendants,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            CreatedBy = currentUser.UserId
        }, ct);
        accessVersion.Bump();
        await audit.WriteAsync(currentUser.UserId, "role.assigned", "RoleAssignment", id.ToString(), request, currentUser.IpAddress, ct);

        var created = (await assignmentQueries.ListAsync(request.UserId, request.OrgUnitId, ct)).First(a => a.RoleAssignmentId == id);
        return OperationResult<RoleAssignmentDto>.Ok(created);
    }

    public async Task<OperationResult> UnassignAsync(long roleAssignmentId, CancellationToken ct = default)
    {
        var assignment = await assignmentQueries.GetByIdAsync(roleAssignmentId, ct);
        if (assignment is null) return OperationResult.Fail(ErrorKind.NotFound, "not_found", "This assignment does not exist.");

        var actorId = currentUser.UserId
            ?? throw new InvalidOperationException("Role assignment requires a signed-in user.");
        if (!await permissionEvaluator.CanAsync(actorId, Permissions.RoleManage, assignment.OrgUnitId, ct))
            return OperationResult.Fail(ErrorKind.Forbidden, "forbidden", "You cannot manage roles on this unit.");

        var systemAdminId = await roleQueries.GetIdByNameAsync(SystemRoles.SystemAdmin, ct);
        if (assignment.RoleId == systemAdminId && await assignmentQueries.CountForRoleAsync(assignment.RoleId, ct) <= 1)
            return OperationResult.Fail(ErrorKind.Validation, "last_admin",
                "This is the last System Admin. Give the role to someone else before removing it.");

        await assignmentCommand.DeleteAsync(roleAssignmentId, ct);
        accessVersion.Bump();
        await audit.WriteAsync(currentUser.UserId, "role.unassigned", "RoleAssignment", roleAssignmentId.ToString(),
            new { assignment.UserId, assignment.RoleId, assignment.OrgUnitId }, currentUser.IpAddress, ct);
        return OperationResult.Ok();
    }

    private static (string Name, List<string> Permissions, string? Error) Normalize(SaveRoleRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100) return (name, [], "Enter a role name of up to 100 characters.");

        var permissions = (request.Permissions ?? []).Select(p => p.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (permissions.Count == 0) return (name, permissions, "Choose at least one permission.");

        var unknown = permissions.Where(p => !Permissions.Exists(p)).ToList();
        if (unknown.Count > 0) return (name, permissions, $"Unknown permissions: {string.Join(", ", unknown)}.");

        return (name, permissions, null);
    }
}
