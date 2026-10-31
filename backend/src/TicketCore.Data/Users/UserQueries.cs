using Dapper;
using Microsoft.EntityFrameworkCore;
using TicketCore.Common;
using TicketCore.Models.Users;
using TicketCore.ViewModels.Users;

namespace TicketCore.Data.Users.Queries;

public interface IUserQueries
{
    Task<bool> AnyAsync(CancellationToken ct = default);
    Task<User?> GetByIdAsync(long userId, CancellationToken ct = default);
    Task<User?> GetByUserNameOrEmailAsync(string userNameOrEmail, CancellationToken ct = default);
    Task<bool> UserNameExistsAsync(string userName, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<PagedResult<UserListItemDto>> SearchAsync(string? search, int page, int pageSize, CancellationToken ct = default);
}

public sealed class UserQueries(TicketDbContext db, ISqlConnectionFactory connections) : IUserQueries
{
    public Task<bool> AnyAsync(CancellationToken ct = default) => db.Users.AnyAsync(ct);

    public Task<User?> GetByIdAsync(long userId, CancellationToken ct = default) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId, ct);

    public Task<User?> GetByUserNameOrEmailAsync(string userNameOrEmail, CancellationToken ct = default) =>
        db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == userNameOrEmail || u.Email == userNameOrEmail, ct);

    public Task<bool> UserNameExistsAsync(string userName, CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.UserName == userName, ct);

    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.Email == email, ct);

    public async Task<PagedResult<UserListItemDto>> SearchAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var pattern = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";

        const string where = """
            WHERE (@pattern IS NULL OR u.UserName LIKE @pattern OR u.Email LIKE @pattern
                   OR u.FirstName LIKE @pattern OR u.LastName LIKE @pattern)
            """;

        await using var conn = await connections.OpenAsync(ct);
        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(1) FROM dbo.[User] u {where}", new { pattern }, cancellationToken: ct));

        var rows = await conn.QueryAsync<UserRow>(new CommandDefinition($"""
            SELECT u.UserId, u.UserName, u.Email, u.FirstName, u.LastName, u.UserType AS UserTypeValue,
                   u.HomeOrgUnitId, o.Name AS HomeOrgUnitName, u.IsActive
            FROM dbo.[User] u
            LEFT JOIN dbo.OrgUnit o ON o.OrgUnitId = u.HomeOrgUnitId
            {where}
            ORDER BY u.UserName
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
            """, new { pattern, skip = (page - 1) * pageSize, take = pageSize }, cancellationToken: ct));

        return new PagedResult<UserListItemDto>(rows.Select(r => r.ToDto()).ToList(), total);
    }

    private sealed class UserRow
    {
        public long UserId { get; init; }
        public string UserName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public byte UserTypeValue { get; init; }
        public int? HomeOrgUnitId { get; init; }
        public string? HomeOrgUnitName { get; init; }
        public bool IsActive { get; init; }

        public UserListItemDto ToDto()
        {
            var name = $"{FirstName} {LastName}".Trim();
            return new UserListItemDto(UserId, UserName, Email, name.Length == 0 ? UserName : name,
                ((UserType)UserTypeValue).ToString(), HomeOrgUnitId, HomeOrgUnitName, IsActive);
        }
    }
}
