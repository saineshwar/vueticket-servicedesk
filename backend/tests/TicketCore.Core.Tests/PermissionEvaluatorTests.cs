using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TicketCore.Core.Access;
using TicketCore.Data.Access.Queries;
using TicketCore.Data.Organization.Queries;
using TicketCore.ViewModels.Access;
using TicketCore.ViewModels.Organization;

namespace TicketCore.Core.Tests;

public class PermissionEvaluatorTests
{
    // Tree: 1 Org → 2 BU → 3 IT dept → 5 Network team; 2 BU → 4 HR dept
    private static readonly (int, int?)[] Tree = [(1, null), (2, 1), (3, 2), (4, 2), (5, 3)];

    private readonly FakeAccessQueries _grants = new();
    private readonly AccessVersion _version = new();
    private readonly PermissionEvaluator _evaluator;

    public PermissionEvaluatorTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<IOrgUnitQueries>(_ => new FakeOrgUnitQueries(Tree));
        var tree = new OrgTreeCache(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
        _evaluator = new PermissionEvaluator(_grants, new MemoryCache(new MemoryCacheOptions()), _version, tree);
    }

    [Fact]
    public async Task Grant_with_sub_units_applies_to_the_whole_subtree()
    {
        _grants.Set(new PermissionGrant("ticket.view", 3, true));

        Assert.True(await _evaluator.CanAsync(7, "ticket.view", 3));
        Assert.True(await _evaluator.CanAsync(7, "ticket.view", 5));
        Assert.False(await _evaluator.CanAsync(7, "ticket.view", 4));
        Assert.False(await _evaluator.CanAsync(7, "ticket.view", 2));
    }

    [Fact]
    public async Task Grant_without_sub_units_applies_to_that_unit_only()
    {
        _grants.Set(new PermissionGrant("ticket.view", 3, false));

        Assert.True(await _evaluator.CanAsync(7, "ticket.view", 3));
        Assert.False(await _evaluator.CanAsync(7, "ticket.view", 5));
    }

    [Fact]
    public async Task Other_permissions_are_not_implied()
    {
        _grants.Set(new PermissionGrant("ticket.view", 1, true));

        Assert.False(await _evaluator.CanAsync(7, "ticket.delete", 5));
        Assert.True((await _evaluator.GetAccessAsync(7)).HasAnywhere("ticket.view"));
        Assert.False((await _evaluator.GetAccessAsync(7)).HasAnywhere("ticket.delete"));
    }

    [Fact]
    public async Task Access_is_cached_until_the_version_changes()
    {
        _grants.Set(new PermissionGrant("ticket.view", 3, true));
        Assert.True(await _evaluator.CanAsync(7, "ticket.view", 5));

        _grants.Set();
        Assert.True(await _evaluator.CanAsync(7, "ticket.view", 5)); // still cached

        _version.Bump();
        Assert.False(await _evaluator.CanAsync(7, "ticket.view", 5));
    }

    private sealed class FakeAccessQueries : IAccessQueries
    {
        private IReadOnlyList<PermissionGrant> _grants = [];
        public void Set(params PermissionGrant[] grants) => _grants = grants;
        public Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(long userId, CancellationToken ct = default) => Task.FromResult(_grants);
    }

    private sealed class FakeOrgUnitQueries((int, int?)[] tree) : IOrgUnitQueries
    {
        public Task<IReadOnlyList<(int OrgUnitId, int? ParentId)>> GetParentMapAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<(int OrgUnitId, int? ParentId)>>(tree);

        public Task<IReadOnlyList<OrgUnitDto>> GetTreeAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrgUnitDto?> GetByIdAsync(int orgUnitId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> CodeExistsAsync(string code, int? excludeOrgUnitId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> IsDescendantOrSelfAsync(int ancestorId, int unitId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasChildrenAsync(int orgUnitId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
