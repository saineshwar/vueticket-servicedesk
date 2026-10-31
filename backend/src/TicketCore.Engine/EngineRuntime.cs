using System.Collections.Concurrent;

namespace TicketCore.Engine;

/// <summary>One piece of automation, run on its own interval by the worker host. Each run gets a fresh DI scope.</summary>
public interface IEngineTask
{
    /// <summary>Does one pass and returns how many items it acted on.</summary>
    Task<int> RunOnceAsync(CancellationToken ct);
}

public sealed class EngineTaskOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 30;
}

/// <summary>Engine settings live in the worker's own configuration, never in database tables.</summary>
public sealed class EngineOptions
{
    public const string SectionName = "Engine";

    public EngineTaskOptions Events { get; set; } = new() { IntervalSeconds = 5 };
    public EngineTaskOptions SlaMonitor { get; set; } = new() { IntervalSeconds = 30 };
    public EngineTaskOptions Escalation { get; set; } = new() { IntervalSeconds = 60 };
    public EngineTaskOptions Assignment { get; set; } = new() { IntervalSeconds = 30 };
    public EngineTaskOptions UnassignedWatch { get; set; } = new() { IntervalSeconds = 60 };
    public EngineTaskOptions AutoClose { get; set; } = new() { IntervalSeconds = 300 };
    public EngineTaskOptions LogCleanup { get; set; } = new() { IntervalSeconds = 3600 };

    /// <summary>How long run history is kept for the admin Engine page.</summary>
    public int RunLogRetentionDays { get; set; } = 30;

    /// <summary>How often calendars, SLA policies and team settings are reloaded from the database.</summary>
    public int ReferenceDataRefreshSeconds { get; set; } = 120;
}

/// <summary>Identifies this worker instance when it takes leases.</summary>
public sealed class EngineInstance
{
    public string Id { get; } = $"{Environment.MachineName}:{Environment.ProcessId}";
    public DateTime StartedAt { get; } = DateTime.UtcNow;
}

public sealed record EngineTaskStatus(string Task, bool Enabled, DateTime? LastRunAt, int LastProcessed, DateTime? LastErrorAt, string? LastError, long Runs);

/// <summary>In-memory health of the engine's tasks, reported by the worker's /status endpoint.</summary>
public sealed class EngineStatus
{
    private readonly ConcurrentDictionary<string, EngineTaskStatus> _tasks = new();

    public void Registered(string task, bool enabled) => _tasks[task] = new EngineTaskStatus(task, enabled, null, 0, null, null, 0);

    public void Succeeded(string task, DateTime at, int processed) =>
        _tasks.AddOrUpdate(task, _ => new EngineTaskStatus(task, true, at, processed, null, null, 1),
            (_, s) => s with { LastRunAt = at, LastProcessed = processed, Runs = s.Runs + 1 });

    public void Failed(string task, DateTime at, string error) =>
        _tasks.AddOrUpdate(task, _ => new EngineTaskStatus(task, true, at, 0, at, error, 1),
            (_, s) => s with { LastRunAt = at, LastErrorAt = at, LastError = error, Runs = s.Runs + 1 });

    public IReadOnlyList<EngineTaskStatus> Snapshot() => _tasks.Values.OrderBy(t => t.Task).ToList();
}
