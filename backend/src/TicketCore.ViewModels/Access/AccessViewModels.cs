namespace TicketCore.ViewModels.Access;

public sealed record MeResponse(
    long UserId,
    string UserName,
    string Email,
    string DisplayName,
    string UserType,
    int? HomeOrgUnitId);

/// <summary>Where a permission applies: one unit, optionally with everything below it.</summary>
public sealed record GrantScope(int OrgUnitId, bool IncludeDescendants);

/// <summary>
/// Effective permissions of the signed-in user, keyed by permission. Grants stay compact (unit + sub-units flag)
/// so a System Admin on the root does not expand to every unit. The UI uses this for menus; per-record actions
/// come from the server on each record.
/// </summary>
public sealed record MyPermissionsResponse(IReadOnlyDictionary<string, IReadOnlyList<GrantScope>> Permissions);

public sealed record AvailabilityResponse(string Status, DateTime? ChangedAt);

public sealed record SetAvailabilityRequest(string Status);

public sealed record PermissionDto(string Key, string Group, string Description);

public sealed record RoleDto(int RoleId, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int AssignmentCount);

public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record RoleAssignmentDto(
    long RoleAssignmentId,
    long UserId,
    string UserDisplayName,
    int RoleId,
    string RoleName,
    int OrgUnitId,
    string OrgUnitName,
    bool IncludeDescendants,
    DateTime CreatedAt);

public sealed record CreateRoleAssignmentRequest(long UserId, int RoleId, int OrgUnitId, bool IncludeDescendants);

/// <summary>A single grant row from the user's role assignments.</summary>
public sealed record PermissionGrant(string PermissionKey, int OrgUnitId, bool IncludeDescendants);
