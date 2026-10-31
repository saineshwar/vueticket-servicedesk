namespace TicketCore.ViewModels.Users;

public sealed record UserListItemDto(
    long UserId,
    string UserName,
    string Email,
    string DisplayName,
    string UserType,
    int? HomeOrgUnitId,
    string? HomeOrgUnitName,
    bool IsActive);

public sealed record CreateUserRequest(
    string UserName,
    string Email,
    string? FirstName,
    string? LastName,
    string UserType,
    int? HomeOrgUnitId,
    long? ManagerUserId,
    string Password);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);
