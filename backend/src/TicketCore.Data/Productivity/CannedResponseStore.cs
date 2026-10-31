using Dapper;

namespace TicketCore.Data.Productivity;

public sealed class CannedResponseRecord
{
    public int CannedResponseId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public long? OwnerUserId { get; init; }
    public int? TeamId { get; init; }
    public string? TeamName { get; init; }
}

public interface ICannedResponseStore
{
    /// <summary>The user's personal replies plus shared replies of the given teams.</summary>
    Task<IReadOnlyList<CannedResponseRecord>> ListAsync(long userId, IReadOnlyCollection<int> teamIds, CancellationToken ct = default);
    Task<CannedResponseRecord?> GetAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(string title, string body, long? ownerUserId, int? teamId, long createdBy, DateTime now, CancellationToken ct = default);
    Task UpdateAsync(int id, string title, string body, DateTime now, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public sealed class CannedResponseStore(ISqlConnectionFactory connections) : ICannedResponseStore
{
    private const string Select = """
        SELECT c.CannedResponseId, c.Title, c.Body, c.OwnerUserId, c.TeamId, o.Name AS TeamName
        FROM dbo.CannedResponse c
        LEFT JOIN dbo.OrgUnit o ON o.OrgUnitId = c.TeamId
        """;

    public async Task<IReadOnlyList<CannedResponseRecord>> ListAsync(long userId, IReadOnlyCollection<int> teamIds, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var ids = teamIds.Count == 0 ? [-1] : teamIds.ToArray();
        var rows = await db.QueryAsync<CannedResponseRecord>(new CommandDefinition(
            Select + " WHERE c.OwnerUserId = @userId OR c.TeamId IN @ids ORDER BY c.Title",
            new { userId, ids }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<CannedResponseRecord?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<CannedResponseRecord>(new CommandDefinition(
            Select + " WHERE c.CannedResponseId = @id", new { id }, cancellationToken: ct));
    }

    public async Task<int> CreateAsync(string title, string body, long? ownerUserId, int? teamId, long createdBy, DateTime now, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO dbo.CannedResponse (Title, Body, OwnerUserId, TeamId, CreatedByUserId, CreatedAt)
            OUTPUT inserted.CannedResponseId
            VALUES (@title, @body, @ownerUserId, @teamId, @createdBy, @now)
            """, new { title, body, ownerUserId, teamId, createdBy, now }, cancellationToken: ct));
    }

    public async Task UpdateAsync(int id, string title, string body, DateTime now, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.CannedResponse SET Title = @title, Body = @body, UpdatedAt = @now WHERE CannedResponseId = @id",
            new { id, title, body, now }, cancellationToken: ct));
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.CannedResponse WHERE CannedResponseId = @id", new { id }, cancellationToken: ct));
    }
}
