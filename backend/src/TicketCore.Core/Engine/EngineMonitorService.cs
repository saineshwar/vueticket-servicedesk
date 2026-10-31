using TicketCore.Data.Engine;
using TicketCore.ViewModels.Engine;
using TicketCore.ViewModels.Users;

namespace TicketCore.Core.Engine;

public interface IEngineMonitorService
{
    Task<EngineOverviewResponse> GetOverviewAsync(CancellationToken ct = default);
    Task<PagedResult<EngineRunDto>> GetRunsAsync(string? task, bool errorsOnly, int page, int pageSize, CancellationToken ct = default);
}

/// <summary>Turns the worker's run history into health an administrator can read at a glance.</summary>
public sealed class EngineMonitorService(IEngineMonitorStore store, TimeProvider clock) : IEngineMonitorService
{
    /// <summary>The event reader runs every few seconds, so an instance silent for this long has stopped.</summary>
    private static readonly TimeSpan InstanceSilence = TimeSpan.FromMinutes(2);

    public async Task<EngineOverviewResponse> GetOverviewAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var tasks = await store.GetTaskStatesAsync(now.AddDays(-1), ct);

        var lastSeen = tasks.GroupBy(t => t.InstanceId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.LastRunAt ?? t.InstanceStartedAt));
        foreach (var t in tasks)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, t.IntervalSeconds));
            if (!t.Enabled) t.Health = "Disabled";
            else if (now - lastSeen[t.InstanceId] > InstanceSilence) t.Health = "Stopped";
            else if (t.LastErrorAt is { } failedAt && (t.LastSuccessAt is null || failedAt > t.LastSuccessAt)) t.Health = "Failing";
            else if (now - (t.LastRunAt ?? t.InstanceStartedAt) > interval * 3 + TimeSpan.FromSeconds(30)) t.Health = "Late";
            else t.Health = "Running";

            if (t.Health is "Running" or "Late" or "Failing")
                t.NextRunAt = (t.LastRunAt ?? t.InstanceStartedAt) + interval;
        }
        return new EngineOverviewResponse(now, tasks, await store.GetStreamAsync(ct));
    }

    public Task<PagedResult<EngineRunDto>> GetRunsAsync(string? task, bool errorsOnly, int page, int pageSize, CancellationToken ct = default) =>
        store.GetRunsAsync(string.IsNullOrWhiteSpace(task) ? null : task.Trim(), errorsOnly, page, pageSize, ct);
}
