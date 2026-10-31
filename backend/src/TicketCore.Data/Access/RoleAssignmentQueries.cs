using Dapper;
using Microsoft.EntityFrameworkCore;
using TicketCore.Models.Access;
using TicketCore.ViewModels.Access;

namespace TicketCore.Data.Access.Queries;

public interface IRoleAssignmentQueries
{
    Task<IReadOnlyList<RoleAssignmentDto>> ListAsync(long? userId, int? orgUnitId, CancellationToken ct = default);
    Task<RoleAssignment?> GetByIdAsync(long roleAssignmentId, CancellationToken ct = default);
    Task<bool> ExistsAsync(long userId, int roleId, int orgUnitId, CancellationToken ct = default);
    Task<int> CountForRoleAsync(int roleId, CancellationToken ct = default);
}

public sealed class RoleAssignmentQueries(TicketDbContext db, ISqlConnectionFactory connections) : IRoleAssignmentQueries
{
    public async Task<IReadOnlyList<RoleAssignmentDto>> ListAsync(long? userId, int? orgUnitId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<AssignmentRow>(new CommandDefinition("""
            SELECT ra.RoleAssignmentId, ra.UserId, u.UserName, u.FirstName, u.LastName,
                   ra.RoleId, r.Name AS RoleName, ra.OrgUnitId, o.Name AS OrgUnitName,
                   ra.IncludeDescendants, ra.CreatedAt
            FROM dbo.RoleAssignment ra
            JOIN dbo.[User] u ON u.UserId = ra.UserId
            JOIN dbo.Role r ON r.RoleId = ra.RoleId
            JOIN dbo.OrgUnit o ON o.OrgUnitId = ra.OrgUnitId
            WHERE (@userId IS NULL OR ra.UserId = @userId)
              AND (@orgUnitId IS NULL OR ra.OrgUnitId = @orgUnitId)
            ORDER BY o.Name, r.Name, u.UserName
            """, new { userId, orgUnitId }, cancellationToken: ct));
        return rows.Select(r => r.ToDto()).ToList();
    }

    public Task<RoleAssignment?> GetByIdAsync(long roleAssignmentId, CancellationToken ct = default) =>
        db.RoleAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.RoleAssignmentId == roleAssignmentId, ct);

    public Task<bool> ExistsAsync(long userId, int roleId, int orgUnitId, CancellationToken ct = default) =>
        db.RoleAssignments.AnyAsync(a => a.UserId == userId && a.RoleId == roleId && a.OrgUnitId == orgUnitId, ct);

    public Task<int> CountForRoleAsync(int roleId, CancellationToken ct = default) =>
        db.RoleAssignments.CountAsync(a => a.RoleId == roleId, ct);

    private sealed class AssignmentRow
    {
        public long RoleAssignmentId { get; init; }
        public long UserId { get; init; }
        public string UserName { get; init; } = string.Empty;
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public int RoleId { get; init; }
        public string RoleName { get; init; } = string.Empty;
        public int OrgUnitId { get; init; }
        public string OrgUnitName { get; init; } = string.Empty;
        public bool IncludeDescendants { get; init; }
        public DateTime CreatedAt { get; init; }

        public RoleAssignmentDto ToDto()
        {
            var name = $"{FirstName} {LastName}".Trim();
            return new RoleAssignmentDto(RoleAssignmentId, UserId, name.Length == 0 ? UserName : name,
                RoleId, RoleName, OrgUnitId, OrgUnitName, IncludeDescendants, CreatedAt);
        }
    }
}
