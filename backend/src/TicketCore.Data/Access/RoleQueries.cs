using Dapper;
using Microsoft.EntityFrameworkCore;
using TicketCore.ViewModels.Access;

namespace TicketCore.Data.Access.Queries;

public interface IRoleQueries
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default);
    Task<RoleDto?> GetByIdAsync(int roleId, CancellationToken ct = default);
    Task<int?> GetIdByNameAsync(string name, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, int? excludeRoleId, CancellationToken ct = default);
}

public sealed class RoleQueries(TicketDbContext db, ISqlConnectionFactory connections) : IRoleQueries
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct);
        var roles = await conn.QueryAsync<RoleRow>(new CommandDefinition("""
            SELECT r.RoleId, r.Name, r.Description, r.IsSystem,
                   (SELECT COUNT(1) FROM dbo.RoleAssignment ra WHERE ra.RoleId = r.RoleId) AS AssignmentCount
            FROM dbo.Role r
            ORDER BY r.IsSystem DESC, r.Name
            """, cancellationToken: ct));
        var perms = (await conn.QueryAsync<(int RoleId, string PermissionKey)>(new CommandDefinition(
            "SELECT RoleId, PermissionKey FROM dbo.RolePermission", cancellationToken: ct)))
            .ToLookup(p => p.RoleId, p => p.PermissionKey);

        return roles.Select(r => new RoleDto(r.RoleId, r.Name, r.Description, r.IsSystem,
            perms[r.RoleId].OrderBy(k => k).ToList(), r.AssignmentCount)).ToList();
    }

    public async Task<RoleDto?> GetByIdAsync(int roleId, CancellationToken ct = default)
    {
        var all = await ListAsync(ct);
        return all.FirstOrDefault(r => r.RoleId == roleId);
    }

    public async Task<int?> GetIdByNameAsync(string name, CancellationToken ct = default) =>
        await db.Roles.Where(r => r.Name == name).Select(r => (int?)r.RoleId).FirstOrDefaultAsync(ct);

    public Task<bool> NameExistsAsync(string name, int? excludeRoleId, CancellationToken ct = default) =>
        db.Roles.AnyAsync(r => r.Name == name && (excludeRoleId == null || r.RoleId != excludeRoleId), ct);

    private sealed class RoleRow
    {
        public int RoleId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public bool IsSystem { get; init; }
        public int AssignmentCount { get; init; }
    }
}
