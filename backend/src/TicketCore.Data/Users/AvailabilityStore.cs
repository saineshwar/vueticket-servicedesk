using Dapper;

namespace TicketCore.Data.Users;

public enum AgentStatus : byte
{
    Available = 1,
    Busy = 2,
    Away = 3,
    Offline = 4
}

public sealed record AvailabilityRow(long UserId, byte Status, DateTime ChangedAt);

/// <summary>Agent check-in state. The API writes it; the engine only reads it when assigning tickets.</summary>
public interface IAvailabilityStore
{
    Task<AvailabilityRow?> GetAsync(long userId, CancellationToken ct = default);
    Task SetAsync(long userId, AgentStatus status, DateTime now, bool automatic, CancellationToken ct = default);
}

public sealed class AvailabilityStore(ISqlConnectionFactory connections) : IAvailabilityStore
{
    public async Task<AvailabilityRow?> GetAsync(long userId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<AvailabilityRow>(new CommandDefinition(
            "SELECT UserId, Status, ChangedAt FROM dbo.AgentAvailability WHERE UserId = @userId", new { userId }, cancellationToken: ct));
    }

    public async Task SetAsync(long userId, AgentStatus status, DateTime now, bool automatic, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            MERGE dbo.AgentAvailability WITH (HOLDLOCK) AS t
            USING (SELECT @userId AS UserId) AS s ON t.UserId = s.UserId
            WHEN MATCHED THEN UPDATE SET Status = @status, ChangedAt = @now
            WHEN NOT MATCHED THEN INSERT (UserId, Status, ChangedAt) VALUES (@userId, @status, @now);
            """, new { userId, status = (byte)status, now }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.AgentAvailabilityLog (UserId, Status, ChangedAt, Automatic) VALUES (@userId, @status, @now, @automatic)
            """, new { userId, status = (byte)status, now, automatic }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }
}
