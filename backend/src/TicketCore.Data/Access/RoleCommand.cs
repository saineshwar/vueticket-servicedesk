using Microsoft.EntityFrameworkCore;
using TicketCore.Models.Access;

namespace TicketCore.Data.Access.Commands;

public interface IRoleCommand
{
    Task<int> CreateAsync(string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default);
    Task<bool> UpdateAsync(int roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default);
    Task<bool> DeleteAsync(int roleId, CancellationToken ct = default);
}

public sealed class RoleCommand(TicketDbContext db) : IRoleCommand
{
    public async Task<int> CreateAsync(string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default)
    {
        var role = new Role
        {
            Name = name,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            Permissions = permissions.Select(p => new RolePermission { PermissionKey = p }).ToList()
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync(ct);
        return role.RoleId;
    }

    public async Task<bool> UpdateAsync(int roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.RoleId == roleId, ct);
        if (role is null) return false;

        role.Name = name;
        role.Description = description;
        role.UpdatedAt = DateTime.UtcNow;

        var wanted = permissions.ToHashSet(StringComparer.Ordinal);
        role.Permissions.RemoveAll(p => !wanted.Contains(p.PermissionKey));
        foreach (var key in wanted.Where(k => role.Permissions.All(p => p.PermissionKey != k)))
        {
            role.Permissions.Add(new RolePermission { RoleId = roleId, PermissionKey = key });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int roleId, CancellationToken ct = default)
    {
        var deleted = await db.Roles.Where(r => r.RoleId == roleId).ExecuteDeleteAsync(ct);
        return deleted == 1;
    }
}
