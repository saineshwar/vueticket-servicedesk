using TicketCore.Common;
using TicketCore.Core.Access;
using TicketCore.Core.Users;
using TicketCore.Data.Access.Commands;
using TicketCore.Data.Access.Queries;
using TicketCore.Data.Audit;
using TicketCore.Data.Organization.Commands;
using TicketCore.Data.Organization.Queries;
using TicketCore.Data.Users.Queries;
using TicketCore.Models.Access;
using TicketCore.ViewModels.Auth;
using TicketCore.ViewModels.Users;

namespace TicketCore.Core.Setup;

public interface ISetupService
{
    Task<bool> IsInitializedAsync(CancellationToken ct = default);

    /// <summary>First-run setup: creates the organization root and the first System Admin. Only works while there are no users.</summary>
    Task<OperationResult> InitializeAsync(SetupRequest request, CancellationToken ct = default);
}

public sealed class SetupService(
    IUserQueries userQueries,
    IUserService userService,
    IOrgUnitQueries orgUnitQueries,
    IOrgUnitCommand orgUnitCommand,
    IRoleQueries roleQueries,
    IRoleAssignmentCommand assignmentCommand,
    AccessVersion accessVersion,
    OrgTreeCache treeCache,
    IAuditCommand audit,
    ICurrentUser currentUser,
    TimeProvider clock) : ISetupService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public Task<bool> IsInitializedAsync(CancellationToken ct = default) => userQueries.AnyAsync(ct);

    public async Task<OperationResult> InitializeAsync(SetupRequest request, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (await userQueries.AnyAsync(ct))
                return OperationResult.Fail(ErrorKind.Conflict, "already_initialized", "VueTicket is already set up.");

            var orgName = request.OrganizationName?.Trim() ?? string.Empty;
            var orgCode = request.OrganizationCode?.Trim().ToUpperInvariant() ?? string.Empty;
            if (orgName.Length is 0 or > 150 || orgCode.Length is 0 or > 30)
                return OperationResult.Fail(ErrorKind.Validation, "invalid_org", "Enter the organization name and a short code.");

            // Reuse the root if an earlier attempt created it but failed afterwards.
            var root = (await orgUnitQueries.GetTreeAsync(ct)).FirstOrDefault(u => u.ParentId is null);
            var rootId = root?.OrgUnitId ?? await orgUnitCommand.CreateAsync(null, OrgUnitType.Organization, orgName, orgCode, ct);
            treeCache.Invalidate();

            var admin = await userService.CreateInitialAdminAsync(new CreateUserRequest(
                request.AdminUserName, request.AdminEmail, request.AdminFirstName, request.AdminLastName,
                nameof(UserType.Staff), rootId, null, request.AdminPassword), ct);
            if (!admin.Succeeded) return admin;

            var roleId = await roleQueries.GetIdByNameAsync(SystemRoles.SystemAdmin, ct)
                ?? throw new InvalidOperationException("The System Admin role is missing. Run the database migrations.");

            // Nobody is signed in yet, so this one assignment bypasses the normal "can you give this role" check.
            var assignmentId = await assignmentCommand.CreateAsync(new RoleAssignment
            {
                UserId = admin.Value!.UserId,
                RoleId = roleId,
                OrgUnitId = rootId,
                IncludeDescendants = true,
                CreatedAt = clock.GetUtcNow().UtcDateTime
            }, ct);
            accessVersion.Bump();
            await audit.WriteAsync(admin.Value.UserId, "setup.completed", "RoleAssignment", assignmentId.ToString(),
                new { organization = orgName, admin = admin.Value.UserName }, currentUser.IpAddress, ct);
            return OperationResult.Ok();
        }
        finally
        {
            Gate.Release();
        }
    }
}
