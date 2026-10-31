using Dapper;
using TicketCore.Common;
using TicketCore.ViewModels.Organization;

namespace TicketCore.Data.Organization.Queries;

public interface IOrgUnitQueries
{
    Task<IReadOnlyList<OrgUnitDto>> GetTreeAsync(CancellationToken ct = default);
    Task<OrgUnitDto?> GetByIdAsync(int orgUnitId, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, int? excludeOrgUnitId, CancellationToken ct = default);
    Task<bool> IsDescendantOrSelfAsync(int ancestorId, int unitId, CancellationToken ct = default);
    Task<bool> HasChildrenAsync(int orgUnitId, CancellationToken ct = default);

    /// <summary>Every unit with its parent, for the in-memory tree used by permission checks.</summary>
    Task<IReadOnlyList<(int OrgUnitId, int? ParentId)>> GetParentMapAsync(CancellationToken ct = default);
}

public sealed class OrgUnitQueries(ISqlConnectionFactory connections) : IOrgUnitQueries
{
    private const string SelectUnits = """
        SELECT u.OrgUnitId, u.ParentId, u.UnitType AS UnitTypeValue, u.Name, u.Code, u.IsActive,
               ISNULL((SELECT MAX(c.Depth) FROM dbo.OrgUnitClosure c WHERE c.DescendantId = u.OrgUnitId), 0) AS Depth,
               (SELECT COUNT(1) FROM dbo.[User] m WHERE m.HomeOrgUnitId = u.OrgUnitId) AS MemberCount
        FROM dbo.OrgUnit u
        """;

    public async Task<IReadOnlyList<OrgUnitDto>> GetTreeAsync(CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<OrgUnitRow>(new CommandDefinition(SelectUnits + " ORDER BY u.Name", cancellationToken: ct));
        return rows.Select(r => r.ToDto()).ToList();
    }

    public async Task<OrgUnitDto?> GetByIdAsync(int orgUnitId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<OrgUnitRow>(
            new CommandDefinition(SelectUnits + " WHERE u.OrgUnitId = @orgUnitId", new { orgUnitId }, cancellationToken: ct));
        return row?.ToDto();
    }

    public async Task<bool> CodeExistsAsync(string code, int? excludeOrgUnitId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.OrgUnit WHERE Code = @code AND (@exclude IS NULL OR OrgUnitId <> @exclude)) THEN 1 ELSE 0 END",
            new { code, exclude = excludeOrgUnitId }, cancellationToken: ct));
    }

    public async Task<bool> IsDescendantOrSelfAsync(int ancestorId, int unitId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.OrgUnitClosure WHERE AncestorId = @ancestorId AND DescendantId = @unitId) THEN 1 ELSE 0 END",
            new { ancestorId, unitId }, cancellationToken: ct));
    }

    public async Task<bool> HasChildrenAsync(int orgUnitId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.OrgUnit WHERE ParentId = @orgUnitId) THEN 1 ELSE 0 END",
            new { orgUnitId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<(int OrgUnitId, int? ParentId)>> GetParentMapAsync(CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<(int, int?)>(new CommandDefinition("SELECT OrgUnitId, ParentId FROM dbo.OrgUnit", cancellationToken: ct));
        return rows.ToList();
    }

    private sealed class OrgUnitRow
    {
        public int OrgUnitId { get; init; }
        public int? ParentId { get; init; }
        public byte UnitTypeValue { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public int Depth { get; init; }
        public int MemberCount { get; init; }

        public OrgUnitDto ToDto() =>
            new(OrgUnitId, ParentId, ((OrgUnitType)UnitTypeValue).ToString(), Name, Code, IsActive, Depth, MemberCount);
    }
}
