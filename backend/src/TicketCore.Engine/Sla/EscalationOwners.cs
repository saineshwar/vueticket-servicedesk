using TicketCore.Common;
using TicketCore.Data.Access.Queries;
using TicketCore.Data.Engine;

namespace TicketCore.Engine.Sla;

/// <summary>
/// Finds who should hear about an escalation level: holders of the level's role on the ticket's team, or the nearest
/// unit above it that has one. If nobody holds the role anywhere up the tree, System Admins are used, so an
/// escalation is never dropped silently.
/// </summary>
public sealed class EscalationOwners(IEngineStore store, IRoleQueries roles)
{
    public async Task<IReadOnlyList<long>> FindAsync(int teamId, int roleId, CancellationToken ct)
    {
        var holders = await store.FindNearestRoleHoldersAsync(teamId, roleId, ct);
        if (holders.Count > 0) return holders;

        var adminRole = await roles.GetIdByNameAsync(SystemRoles.SystemAdmin, ct);
        return adminRole is { } id ? await store.FindNearestRoleHoldersAsync(teamId, id, ct) : [];
    }
}
