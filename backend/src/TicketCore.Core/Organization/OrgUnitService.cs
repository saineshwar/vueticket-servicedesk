using System.Text.RegularExpressions;
using TicketCore.Common;
using TicketCore.Core.Access;
using TicketCore.Data.Audit;
using TicketCore.Data.Organization.Commands;
using TicketCore.Data.Organization.Queries;
using TicketCore.ViewModels.Organization;

namespace TicketCore.Core.Organization;

public interface IOrgUnitService
{
    Task<OperationResult<OrgUnitDto>> CreateAsync(CreateOrgUnitRequest request, CancellationToken ct = default);
    Task<OperationResult<OrgUnitDto>> UpdateAsync(int orgUnitId, UpdateOrgUnitRequest request, CancellationToken ct = default);
    Task<OperationResult<OrgUnitDto>> MoveAsync(int orgUnitId, MoveOrgUnitRequest request, CancellationToken ct = default);
}

public sealed partial class OrgUnitService(
    IOrgUnitQueries queries,
    IOrgUnitCommand command,
    OrgTreeCache treeCache,
    IPermissionEvaluator permissions,
    IAuditCommand audit,
    ICurrentUser currentUser) : IOrgUnitService
{
    [GeneratedRegex("^[A-Z0-9][A-Z0-9_-]{0,29}$")]
    private static partial Regex CodePattern();

    /// <summary>A child must sit lower in the hierarchy than its parent; a team cannot have children.</summary>
    public static string? CheckPlacement(OrgUnitType parentType, OrgUnitType childType)
    {
        if (parentType == OrgUnitType.Team) return "A team cannot contain other units.";
        if (childType == OrgUnitType.Organization) return "There can only be one organization, at the top of the tree.";
        if (childType <= parentType) return $"A {Describe(childType)} cannot be placed under a {Describe(parentType)}.";
        return null;
    }

    public async Task<OperationResult<OrgUnitDto>> CreateAsync(CreateOrgUnitRequest request, CancellationToken ct = default)
    {
        if (!Enum.TryParse<OrgUnitType>(request.UnitType, true, out var type))
            return Fail(ErrorKind.Validation, "invalid_type", "Choose Business unit, Department or Team.");

        var name = request.Name?.Trim() ?? string.Empty;
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        var invalid = ValidateNameAndCode(name, code);
        if (invalid is not null) return invalid;

        var parent = await queries.GetByIdAsync(request.ParentId, ct);
        if (parent is null) return Fail(ErrorKind.Validation, "parent_not_found", "The parent unit does not exist.");
        if (!await CanManageAsync(parent.OrgUnitId, ct))
            return Fail(ErrorKind.Forbidden, "forbidden", "You cannot add units here.");

        var placement = CheckPlacement(Enum.Parse<OrgUnitType>(parent.UnitType), type);
        if (placement is not null) return Fail(ErrorKind.Validation, "invalid_placement", placement);

        if (await queries.CodeExistsAsync(code, null, ct))
            return Fail(ErrorKind.Conflict, "code_taken", $"The code {code} is already used by another unit.");

        var id = await command.CreateAsync(parent.OrgUnitId, type, name, code, ct);
        treeCache.Invalidate();
        await audit.WriteAsync(currentUser.UserId, "org.unit.created", "OrgUnit", id.ToString(),
            new { name, code, type = type.ToString(), parentId = parent.OrgUnitId }, currentUser.IpAddress, ct);

        return OperationResult<OrgUnitDto>.Ok((await queries.GetByIdAsync(id, ct))!);
    }

    public async Task<OperationResult<OrgUnitDto>> UpdateAsync(int orgUnitId, UpdateOrgUnitRequest request, CancellationToken ct = default)
    {
        var unit = await queries.GetByIdAsync(orgUnitId, ct);
        if (unit is null) return Fail(ErrorKind.NotFound, "not_found", "This unit does not exist.");
        if (!await CanManageAsync(orgUnitId, ct)) return Fail(ErrorKind.Forbidden, "forbidden", "You cannot change this unit.");

        var name = request.Name?.Trim() ?? string.Empty;
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        var invalid = ValidateNameAndCode(name, code);
        if (invalid is not null) return invalid;

        if (unit.ParentId is null && !request.IsActive)
            return Fail(ErrorKind.Validation, "root_inactive", "The organization itself cannot be deactivated.");

        if (await queries.CodeExistsAsync(code, orgUnitId, ct))
            return Fail(ErrorKind.Conflict, "code_taken", $"The code {code} is already used by another unit.");

        await command.UpdateAsync(orgUnitId, name, code, request.IsActive, ct);
        await audit.WriteAsync(currentUser.UserId, "org.unit.updated", "OrgUnit", orgUnitId.ToString(),
            new { before = new { unit.Name, unit.Code, unit.IsActive }, after = new { name, code, request.IsActive } }, currentUser.IpAddress, ct);

        return OperationResult<OrgUnitDto>.Ok((await queries.GetByIdAsync(orgUnitId, ct))!);
    }

    public async Task<OperationResult<OrgUnitDto>> MoveAsync(int orgUnitId, MoveOrgUnitRequest request, CancellationToken ct = default)
    {
        var unit = await queries.GetByIdAsync(orgUnitId, ct);
        if (unit is null) return Fail(ErrorKind.NotFound, "not_found", "This unit does not exist.");
        if (unit.ParentId is null) return Fail(ErrorKind.Validation, "root_move", "The organization cannot be moved.");
        if (unit.ParentId == request.NewParentId) return OperationResult<OrgUnitDto>.Ok(unit);

        var newParent = await queries.GetByIdAsync(request.NewParentId, ct);
        if (newParent is null) return Fail(ErrorKind.Validation, "parent_not_found", "The new parent unit does not exist.");
        if (!await CanManageAsync(orgUnitId, ct) || !await CanManageAsync(newParent.OrgUnitId, ct))
            return Fail(ErrorKind.Forbidden, "forbidden", "You need to manage both this unit and its new parent to move it.");

        if (await queries.IsDescendantOrSelfAsync(orgUnitId, request.NewParentId, ct))
            return Fail(ErrorKind.Validation, "cycle", "A unit cannot be moved inside itself.");

        var placement = CheckPlacement(Enum.Parse<OrgUnitType>(newParent.UnitType), Enum.Parse<OrgUnitType>(unit.UnitType));
        if (placement is not null) return Fail(ErrorKind.Validation, "invalid_placement", placement);

        await command.MoveAsync(orgUnitId, request.NewParentId, ct);
        treeCache.Invalidate();
        await audit.WriteAsync(currentUser.UserId, "org.unit.moved", "OrgUnit", orgUnitId.ToString(),
            new { from = unit.ParentId, to = request.NewParentId }, currentUser.IpAddress, ct);

        return OperationResult<OrgUnitDto>.Ok((await queries.GetByIdAsync(orgUnitId, ct))!);
    }

    private Task<bool> CanManageAsync(int orgUnitId, CancellationToken ct) =>
        currentUser.UserId is { } actorId
            ? permissions.CanAsync(actorId, Permissions.OrgManage, orgUnitId, ct)
            : Task.FromResult(false);

    private static OperationResult<OrgUnitDto>? ValidateNameAndCode(string name, string code)
    {
        if (name.Length is 0 or > 150) return Fail(ErrorKind.Validation, "invalid_name", "Enter a name of up to 150 characters.");
        if (!CodePattern().IsMatch(code))
            return Fail(ErrorKind.Validation, "invalid_code", "Use a code of up to 30 capital letters, digits, dashes or underscores, for example IT-NET.");
        return null;
    }

    private static string Describe(OrgUnitType type) => type switch
    {
        OrgUnitType.BusinessUnit => "business unit",
        _ => type.ToString().ToLowerInvariant()
    };

    private static OperationResult<OrgUnitDto> Fail(ErrorKind kind, string code, string message) =>
        OperationResult<OrgUnitDto>.Fail(kind, code, message);
}
