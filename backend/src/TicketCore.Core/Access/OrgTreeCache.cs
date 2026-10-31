using Microsoft.Extensions.DependencyInjection;
using TicketCore.Data.Organization.Queries;

namespace TicketCore.Core.Access;

/// <summary>
/// In-memory copy of the organization tree (unit → parent), used to check whether a unit sits under a granted unit.
/// Loaded on first use and reloaded after any change to the tree.
/// For several API nodes this moves to a Redis-backed version stamp (Phase 6).
/// </summary>
public sealed class OrgTreeCache(IServiceScopeFactory scopeFactory)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile IReadOnlyDictionary<int, int?>? _parents;

    public void Invalidate() => _parents = null;

    public async Task<bool> IsSameOrDescendantAsync(int ancestorId, int unitId, CancellationToken ct = default)
    {
        var parents = await GetParentsAsync(ct);
        int? current = unitId;
        var guard = 0;
        while (current is { } id && guard++ < 64)
        {
            if (id == ancestorId) return true;
            current = parents.TryGetValue(id, out var parent) ? parent : null;
        }
        return false;
    }

    private async Task<IReadOnlyDictionary<int, int?>> GetParentsAsync(CancellationToken ct)
    {
        var snapshot = _parents;
        if (snapshot is not null) return snapshot;

        await _lock.WaitAsync(ct);
        try
        {
            if (_parents is not null) return _parents;
            await using var scope = scopeFactory.CreateAsyncScope();
            var queries = scope.ServiceProvider.GetRequiredService<IOrgUnitQueries>();
            var map = await queries.GetParentMapAsync(ct);
            _parents = map.ToDictionary(x => x.OrgUnitId, x => x.ParentId);
            return _parents;
        }
        finally
        {
            _lock.Release();
        }
    }
}
