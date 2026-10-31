using Microsoft.AspNetCore.Authorization;
using TicketCore.Core;
using TicketCore.Core.Access;

namespace TicketCore.Api.Infrastructure;

/// <summary>
/// Requires at least one of the listed permissions somewhere in the organization. Unit-level checks
/// (for example "org.manage on this unit") happen in the Core services, which know which unit a request touches.
/// Stacking several attributes means all of them must pass.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(params string[] anyOf) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public IReadOnlyList<string> AnyOf { get; } = anyOf;

    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new PermissionRequirement(AnyOf)];
}

public sealed record PermissionRequirement(IReadOnlyList<string> AnyOf) : IAuthorizationRequirement;

public sealed class PermissionHandler(IPermissionEvaluator evaluator, ICurrentUser currentUser)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (currentUser.UserId is not { } userId) return;
        var access = await evaluator.GetAccessAsync(userId);
        if (requirement.AnyOf.Any(access.HasAnywhere)) context.Succeed(requirement);
    }
}
