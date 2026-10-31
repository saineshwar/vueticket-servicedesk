using Dapper;
using TicketCore.ViewModels.Access;

namespace TicketCore.Data.Access.Queries;

public interface IAccessQueries
{
    /// <summary>All (permission, unit, include sub-units) grants coming from the user's role assignments.</summary>
    Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(long userId, CancellationToken ct = default);
}

public sealed class AccessQueries(ISqlConnectionFactory connections) : IAccessQueries
{
    public async Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(long userId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<PermissionGrant>(new CommandDefinition("""
            SELECT rp.PermissionKey, ra.OrgUnitId, CAST(MAX(CAST(ra.IncludeDescendants AS INT)) AS BIT) AS IncludeDescendants
            FROM dbo.RoleAssignment ra
            JOIN dbo.RolePermission rp ON rp.RoleId = ra.RoleId
            JOIN dbo.[User] u ON u.UserId = ra.UserId AND u.IsActive = 1
            WHERE ra.UserId = @userId
            GROUP BY rp.PermissionKey, ra.OrgUnitId
            """, new { userId }, cancellationToken: ct));
        return rows.ToList();
    }
}
