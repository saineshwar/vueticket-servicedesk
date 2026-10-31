namespace TicketCore.ViewModels.Engine;

/// <summary>Latest state of one task on one worker instance.</summary>
public sealed class EngineTaskStateDto
{
    public string InstanceId { get; init; } = string.Empty;
    public string Task { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public int IntervalSeconds { get; init; }
    public DateTime InstanceStartedAt { get; init; }
    public DateTime? LastRunAt { get; init; }
    public int? LastDurationMs { get; init; }
    public int LastProcessed { get; init; }
    public DateTime? LastSuccessAt { get; init; }
    public DateTime? LastErrorAt { get; init; }
    public string? LastError { get; init; }
    public long Runs { get; init; }
    public long Failures { get; init; }
    /// <summary>Running, Late (missed several intervals), Failing (last run failed), Stopped (instance gone quiet) or Disabled. Set by the API.</summary>
    public string Health { get; set; } = string.Empty;
    /// <summary>When the next run is expected, from the last run and the interval.</summary>
    public DateTime? NextRunAt { get; set; }
}

/// <summary>The ticket-history event stream: how far the engine has read, and who holds the reader lease.</summary>
public sealed class EngineStreamDto
{
    public long LastEventId { get; init; }
    public long LatestEventId { get; init; }
    public long Backlog => Math.Max(0, LatestEventId - LastEventId);
    public string? LeaseOwner { get; init; }
    public DateTime? LeaseUntil { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record EngineOverviewResponse(DateTime Now, IReadOnlyList<EngineTaskStateDto> Tasks, EngineStreamDto? Stream);

public sealed class EngineRunDto
{
    public long EngineRunLogId { get; init; }
    public string InstanceId { get; init; } = string.Empty;
    public string Task { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }
    public int DurationMs { get; init; }
    public int Processed { get; init; }
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
}
