using Dapper;
using TicketCore.ViewModels.Engine;
using TicketCore.ViewModels.Users;

namespace TicketCore.Data.Engine;

public sealed record EngineRunRecord(string InstanceId, string Task, DateTime StartedAt, int DurationMs, int Processed, string? Error);

/// <summary>
/// Run history for the admin Engine page. The worker writes (after each run, and never lets a failure here stop it);
/// the API reads.
/// </summary>
public interface IEngineMonitorStore
{
    Task RegisterTaskAsync(string instanceId, string task, bool enabled, int intervalSeconds, DateTime instanceStartedAt, CancellationToken ct);
    Task RecordRunAsync(EngineRunRecord run, CancellationToken ct);
    /// <summary>Deletes run log rows older than the cutoff and task states not heard from since then.</summary>
    Task<int> PruneAsync(DateTime before, CancellationToken ct);

    Task<IReadOnlyList<EngineTaskStateDto>> GetTaskStatesAsync(DateTime seenSince, CancellationToken ct = default);
    Task<EngineStreamDto?> GetStreamAsync(CancellationToken ct = default);
    Task<PagedResult<EngineRunDto>> GetRunsAsync(string? task, bool errorsOnly, int page, int pageSize, CancellationToken ct = default);
}

public sealed class EngineMonitorStore(ISqlConnectionFactory connections) : IEngineMonitorStore
{
    private const int MaxError = 1000;

    public async Task RegisterTaskAsync(string instanceId, string task, bool enabled, int intervalSeconds, DateTime instanceStartedAt, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            MERGE dbo.EngineTaskState WITH (HOLDLOCK) AS s
            USING (SELECT @instanceId AS InstanceId, @task AS Task) AS k ON s.InstanceId = k.InstanceId AND s.Task = k.Task
            WHEN MATCHED THEN UPDATE SET Enabled = @enabled, IntervalSeconds = @intervalSeconds, InstanceStartedAt = @instanceStartedAt
            WHEN NOT MATCHED THEN INSERT (InstanceId, Task, Enabled, IntervalSeconds, InstanceStartedAt)
                                  VALUES (@instanceId, @task, @enabled, @intervalSeconds, @instanceStartedAt);
            """, new { instanceId, task, enabled, intervalSeconds, instanceStartedAt }, cancellationToken: ct));
    }

    public async Task RecordRunAsync(EngineRunRecord run, CancellationToken ct)
    {
        var error = run.Error is { Length: > MaxError } e ? e[..MaxError] : run.Error;
        var failed = error is not null;
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.EngineTaskState
            SET LastRunAt = @StartedAt, LastDurationMs = @DurationMs, LastProcessed = @Processed, Runs = Runs + 1,
                LastSuccessAt = CASE WHEN @failed = 0 THEN @StartedAt ELSE LastSuccessAt END,
                LastErrorAt = CASE WHEN @failed = 1 THEN @StartedAt ELSE LastErrorAt END,
                LastError = CASE WHEN @failed = 1 THEN @error ELSE LastError END,
                Failures = Failures + @failed
            WHERE InstanceId = @InstanceId AND Task = @Task;

            -- Only runs that did something or failed are kept in the log.
            IF @Processed > 0 OR @failed = 1
                INSERT INTO dbo.EngineRunLog (InstanceId, Task, StartedAt, DurationMs, Processed, Succeeded, Error)
                VALUES (@InstanceId, @Task, @StartedAt, @DurationMs, @Processed, 1 - @failed, @error);
            """, new { run.InstanceId, run.Task, run.StartedAt, run.DurationMs, run.Processed, failed = failed ? 1 : 0, error }, cancellationToken: ct));
    }

    public async Task<int> PruneAsync(DateTime before, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteAsync(new CommandDefinition("""
            DELETE TOP (5000) FROM dbo.EngineRunLog WHERE StartedAt < @before;
            DELETE FROM dbo.EngineTaskState WHERE ISNULL(LastRunAt, InstanceStartedAt) < @before;
            """, new { before }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<EngineTaskStateDto>> GetTaskStatesAsync(DateTime seenSince, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<EngineTaskStateDto>(new CommandDefinition("""
            SELECT InstanceId, Task, Enabled, IntervalSeconds, InstanceStartedAt, LastRunAt, LastDurationMs, LastProcessed,
                   LastSuccessAt, LastErrorAt, LastError, Runs, Failures
            FROM dbo.EngineTaskState
            WHERE ISNULL(LastRunAt, InstanceStartedAt) >= @seenSince
            ORDER BY InstanceStartedAt DESC, InstanceId, Task
            """, new { seenSince }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<EngineStreamDto?> GetStreamAsync(CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<EngineStreamDto>(new CommandDefinition("""
            SELECT c.LastEventId, ISNULL((SELECT MAX(TicketHistoryId) FROM dbo.TicketHistory), 0) AS LatestEventId,
                   c.LeaseOwner, c.LeaseUntil, c.UpdatedAt
            FROM dbo.EngineCheckpoint c WHERE c.Name = 'ticket-history'
            """, cancellationToken: ct));
    }

    public async Task<PagedResult<EngineRunDto>> GetRunsAsync(string? task, bool errorsOnly, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var args = new { task, errorsOnly, skip = (page - 1) * pageSize, take = pageSize };
        const string where = "WHERE (@task IS NULL OR Task = @task) AND (@errorsOnly = 0 OR Succeeded = 0)";
        await using var db = await connections.OpenAsync(ct);
        var total = await db.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(1) FROM dbo.EngineRunLog {where}", args, cancellationToken: ct));
        var rows = await db.QueryAsync<EngineRunDto>(new CommandDefinition($"""
            SELECT EngineRunLogId, InstanceId, Task, StartedAt, DurationMs, Processed, Succeeded, Error
            FROM dbo.EngineRunLog {where}
            ORDER BY StartedAt DESC, EngineRunLogId DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
            """, args, cancellationToken: ct));
        return new PagedResult<EngineRunDto>(rows.ToList(), total);
    }
}
