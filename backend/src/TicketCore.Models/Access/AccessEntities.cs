namespace TicketCore.Models.Access;

public class Permission
{
    public string PermissionKey { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class Role
{
    public int RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<RolePermission> Permissions { get; set; } = [];
}

public class RolePermission
{
    public int RoleId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
}

public class RoleAssignment
{
    public long RoleAssignmentId { get; set; }
    public long UserId { get; set; }
    public int RoleId { get; set; }
    public int OrgUnitId { get; set; }
    public bool IncludeDescendants { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
}

public class RefreshToken
{
    public long RefreshTokenId { get; set; }
    public long UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public Guid FamilyId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
}

public class AuditLog
{
    public long AuditLogId { get; set; }
    public DateTime OccurredAt { get; set; }
    public long? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
}
