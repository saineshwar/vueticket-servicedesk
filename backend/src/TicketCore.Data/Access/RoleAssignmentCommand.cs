using Microsoft.EntityFrameworkCore;
using TicketCore.Models.Access;

namespace TicketCore.Data.Access.Commands;

public interface IRoleAssignmentCommand
{
    Task<long> CreateAsync(RoleAssignment assignment, CancellationToken ct = default);
    Task<bool> DeleteAsync(long roleAssignmentId, CancellationToken ct = default);
}

public sealed class RoleAssignmentCommand(TicketDbContext db) : IRoleAssignmentCommand
{
    public async Task<long> CreateAsync(RoleAssignment assignment, CancellationToken ct = default)
    {
        db.RoleAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);
        return assignment.RoleAssignmentId;
    }

    public async Task<bool> DeleteAsync(long roleAssignmentId, CancellationToken ct = default) =>
        await db.RoleAssignments.Where(a => a.RoleAssignmentId == roleAssignmentId).ExecuteDeleteAsync(ct) == 1;
}
