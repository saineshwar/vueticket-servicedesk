namespace TicketCore.ViewModels.ServiceDesks;

public sealed record LookupItem(int Id, string Name);

public sealed record LookupsResponse(IReadOnlyList<LookupItem> Priorities, IReadOnlyList<LookupItem> TicketTypes);

public sealed record CategoryDto(
    int CategoryId,
    int ServiceDeskId,
    string Name,
    string? Description,
    int? TeamId,
    string? TeamName,
    int TicketTypeId,
    string TicketTypeName,
    int DefaultPriorityId,
    int SortOrder,
    bool IsActive);

public sealed record ServiceDeskDto(
    int ServiceDeskId,
    string Name,
    string Code,
    string? Description,
    string Audience,
    int DefaultTeamId,
    string DefaultTeamName,
    bool IsActive,
    IReadOnlyList<CategoryDto> Categories,
    int AutoCloseHours);

/// <summary>AutoCloseHours: resolved tickets close after this many hours without a requester reply; 0 never. Null keeps the current value (48 for a new desk).</summary>
public sealed record SaveServiceDeskRequest(string Name, string Code, string? Description, string Audience, int DefaultTeamId, bool IsActive, int? AutoCloseHours = null);

public sealed record SaveCategoryRequest(
    string Name,
    string? Description,
    int? TeamId,
    int TicketTypeId,
    int DefaultPriorityId,
    int SortOrder,
    bool IsActive);
