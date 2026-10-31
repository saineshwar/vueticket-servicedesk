namespace TicketCore.ViewModels.Organization;

public sealed record OrgUnitDto(
    int OrgUnitId,
    int? ParentId,
    string UnitType,
    string Name,
    string Code,
    bool IsActive,
    int Depth,
    int MemberCount);

public sealed record CreateOrgUnitRequest(int ParentId, string UnitType, string Name, string Code);

public sealed record UpdateOrgUnitRequest(string Name, string Code, bool IsActive);

public sealed record MoveOrgUnitRequest(int NewParentId);
