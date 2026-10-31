using Dapper;
using TicketCore.Common;

namespace TicketCore.Data.Organization.Commands;

public interface IOrgUnitCommand
{
    /// <summary>Inserts the unit and its closure rows in one transaction. Returns the new ID.</summary>
    Task<int> CreateAsync(int? parentId, OrgUnitType unitType, string name, string code, CancellationToken ct = default);

    Task<bool> UpdateAsync(int orgUnitId, string name, string code, bool isActive, CancellationToken ct = default);

    /// <summary>Re-parents a unit and its whole subtree. The caller must ensure the new parent is not inside the subtree.</summary>
    Task MoveAsync(int orgUnitId, int newParentId, CancellationToken ct = default);
}

public sealed class OrgUnitCommand(ISqlConnectionFactory connections) : IOrgUnitCommand
{
    public async Task<int> CreateAsync(int? parentId, OrgUnitType unitType, string name, string code, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        var id = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO dbo.OrgUnit (ParentId, UnitType, Name, Code)
            OUTPUT inserted.OrgUnitId
            VALUES (@parentId, @unitType, @name, @code)
            """, new { parentId, unitType = (byte)unitType, name, code }, tx, cancellationToken: ct));

        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.OrgUnitClosure (AncestorId, DescendantId, Depth)
            SELECT AncestorId, @id, Depth + 1 FROM dbo.OrgUnitClosure WHERE DescendantId = @parentId
            UNION ALL
            SELECT @id, @id, 0
            """, new { id, parentId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateAsync(int orgUnitId, string name, string code, bool isActive, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.OrgUnit SET Name = @name, Code = @code, IsActive = @isActive, UpdatedAt = SYSUTCDATETIME()
            WHERE OrgUnitId = @orgUnitId
            """, new { orgUnitId, name, code, isActive }, cancellationToken: ct));
        return rows == 1;
    }

    public async Task MoveAsync(int orgUnitId, int newParentId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        // Detach the subtree from its old ancestors (keep links inside the subtree).
        await db.ExecuteAsync(new CommandDefinition("""
            DELETE FROM dbo.OrgUnitClosure
            WHERE DescendantId IN (SELECT DescendantId FROM dbo.OrgUnitClosure WHERE AncestorId = @orgUnitId)
              AND AncestorId IN (SELECT AncestorId FROM dbo.OrgUnitClosure WHERE DescendantId = @orgUnitId AND AncestorId <> @orgUnitId)
            """, new { orgUnitId }, tx, cancellationToken: ct));

        // Attach it under every ancestor of the new parent.
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.OrgUnitClosure (AncestorId, DescendantId, Depth)
            SELECT p.AncestorId, s.DescendantId, p.Depth + s.Depth + 1
            FROM dbo.OrgUnitClosure p
            CROSS JOIN dbo.OrgUnitClosure s
            WHERE p.DescendantId = @newParentId AND s.AncestorId = @orgUnitId
            """, new { orgUnitId, newParentId }, tx, cancellationToken: ct));

        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.OrgUnit SET ParentId = @newParentId, UpdatedAt = SYSUTCDATETIME() WHERE OrgUnitId = @orgUnitId",
            new { orgUnitId, newParentId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
    }
}
