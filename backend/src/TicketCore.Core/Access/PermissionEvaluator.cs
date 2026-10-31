using Microsoft.Extensions.Caching.Memory;
using TicketCore.Data.Access.Queries;
using TicketCore.ViewModels.Access;

namespace TicketCore.Core.Access;

/// <summary>Bumped whenever roles or role assignments change, so cached access is rebuilt.</summary>
public sealed class AccessVersion
{
    private long _value;
    public long Current => Interlocked.Read(ref _value);
    public void Bump() => Interlocked.Increment(ref _value);
}

/// <summary>A user's grants, grouped by permission key.</summary>
public sealed class UserAccess(IReadOnlyDictionary<string, IReadOnlyList<GrantScope>> grants)
{
    public IReadOnlyDictionary<string, IReadOnlyList<GrantScope>> Grants { get; } = grants;

    /// <summary>True when the permission is granted anywhere in the organization.</summary>
    public bool HasAnywhere(string permissionKey) => Grants.ContainsKey(permissionKey);
}

public interface IPermissionEvaluator
{
    Task<UserAccess> GetAccessAsync(long userId, CancellationToken ct = default);

    /// <summary>True when the user holds the permission on this unit, directly or through an ancestor grant.</summary>
    Task<bool> CanAsync(long userId, string permissionKey, int orgUnitId, CancellationToken ct = default);
}

public sealed class PermissionEvaluator(
    IAccessQueries accessQueries,
    IMemoryCache cache,
    AccessVersion version,
    OrgTreeCache tree) : IPermissionEvaluator
{
    public async Task<UserAccess> GetAccessAsync(long userId, CancellationToken ct = default)
    {
        var key = $"access:{userId}:{version.Current}";
        if (cache.TryGetValue(key, out UserAccess? cached) && cached is not null) return cached;

        var grants = await accessQueries.GetGrantsAsync(userId, ct);
        var access = new UserAccess(grants
            .GroupBy(g => g.PermissionKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<GrantScope>)g.Select(x => new GrantScope(x.OrgUnitId, x.IncludeDescendants)).ToList(),
                StringComparer.Ordinal));

        cache.Set(key, access, TimeSpan.FromMinutes(5));
        return access;
    }

    public async Task<bool> CanAsync(long userId, string permissionKey, int orgUnitId, CancellationToken ct = default)
    {
        var access = await GetAccessAsync(userId, ct);
        if (!access.Grants.TryGetValue(permissionKey, out var scopes)) return false;

        foreach (var scope in scopes)
        {
            if (scope.OrgUnitId == orgUnitId) return true;
            if (scope.IncludeDescendants && await tree.IsSameOrDescendantAsync(scope.OrgUnitId, orgUnitId, ct)) return true;
        }
        return false;
    }
}
