using Dapper;
using TicketCore.Common;
using TicketCore.ViewModels.ServiceDesks;

namespace TicketCore.Data.ServiceDesks.Queries;

public interface IServiceDeskQueries
{
    Task<IReadOnlyList<ServiceDeskDto>> ListAsync(bool activeOnly, CancellationToken ct = default);
    Task<ServiceDeskDto?> GetAsync(int serviceDeskId, CancellationToken ct = default);
    Task<CategoryDto?> GetCategoryAsync(int categoryId, CancellationToken ct = default);
    Task<bool> CodeOrNameExistsAsync(string code, string name, int? excludeId, CancellationToken ct = default);
    Task<bool> CategoryNameExistsAsync(int serviceDeskId, string name, int? excludeCategoryId, CancellationToken ct = default);
    Task<LookupsResponse> GetLookupsAsync(CancellationToken ct = default);
}

public sealed class ServiceDeskQueries(ISqlConnectionFactory connections) : IServiceDeskQueries
{
    private const string SelectCategories = """
        SELECT c.CategoryId, c.ServiceDeskId, c.Name, c.Description, c.TeamId, o.Name AS TeamName,
               CAST(c.TicketTypeId AS INT) AS TicketTypeId, tt.Name AS TicketTypeName, CAST(c.DefaultPriorityId AS INT) AS DefaultPriorityId, c.SortOrder, c.IsActive
        FROM dbo.Category c
        JOIN dbo.TicketType tt ON tt.TicketTypeId = c.TicketTypeId
        LEFT JOIN dbo.OrgUnit o ON o.OrgUnitId = c.TeamId
        """;

    public async Task<IReadOnlyList<ServiceDeskDto>> ListAsync(bool activeOnly, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var desks = await db.QueryAsync<DeskRow>(new CommandDefinition("""
            SELECT d.ServiceDeskId, d.Name, d.Code, d.Description, d.Audience, d.DefaultTeamId, o.Name AS DefaultTeamName, d.IsActive, d.AutoCloseHours
            FROM dbo.ServiceDesk d
            JOIN dbo.OrgUnit o ON o.OrgUnitId = d.DefaultTeamId
            WHERE (@activeOnly = 0 OR d.IsActive = 1)
            ORDER BY d.Name
            """, new { activeOnly }, cancellationToken: ct));

        var categories = (await db.QueryAsync<CategoryDto>(new CommandDefinition(
            SelectCategories + " WHERE (@activeOnly = 0 OR c.IsActive = 1) ORDER BY c.SortOrder, c.Name",
            new { activeOnly }, cancellationToken: ct))).ToLookup(c => c.ServiceDeskId);

        return desks.Select(d => d.ToDto(categories[d.ServiceDeskId].ToList())).ToList();
    }

    public async Task<ServiceDeskDto?> GetAsync(int serviceDeskId, CancellationToken ct = default) =>
        (await ListAsync(false, ct)).FirstOrDefault(d => d.ServiceDeskId == serviceDeskId);

    public async Task<CategoryDto?> GetCategoryAsync(int categoryId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<CategoryDto>(new CommandDefinition(
            SelectCategories + " WHERE c.CategoryId = @categoryId", new { categoryId }, cancellationToken: ct));
    }

    public async Task<bool> CodeOrNameExistsAsync(string code, string name, int? excludeId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.ServiceDesk
                WHERE (Code = @code OR Name = @name) AND (@excludeId IS NULL OR ServiceDeskId <> @excludeId)) THEN 1 ELSE 0 END
            """, new { code, name, excludeId }, cancellationToken: ct));
    }

    public async Task<bool> CategoryNameExistsAsync(int serviceDeskId, string name, int? excludeCategoryId, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Category
                WHERE ServiceDeskId = @serviceDeskId AND Name = @name AND (@excludeCategoryId IS NULL OR CategoryId <> @excludeCategoryId)) THEN 1 ELSE 0 END
            """, new { serviceDeskId, name, excludeCategoryId }, cancellationToken: ct));
    }

    public async Task<LookupsResponse> GetLookupsAsync(CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var priorities = await db.QueryAsync<LookupItem>(new CommandDefinition(
            "SELECT CAST(PriorityId AS INT) AS Id, Name FROM dbo.Priority ORDER BY SortOrder", cancellationToken: ct));
        var types = await db.QueryAsync<LookupItem>(new CommandDefinition(
            "SELECT CAST(TicketTypeId AS INT) AS Id, Name FROM dbo.TicketType ORDER BY TicketTypeId", cancellationToken: ct));
        return new LookupsResponse(priorities.ToList(), types.ToList());
    }

    private sealed class DeskRow
    {
        public int ServiceDeskId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public string? Description { get; init; }
        public byte Audience { get; init; }
        public int DefaultTeamId { get; init; }
        public string DefaultTeamName { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public int AutoCloseHours { get; init; }

        public ServiceDeskDto ToDto(IReadOnlyList<CategoryDto> categories) =>
            new(ServiceDeskId, Name, Code, Description, ((DeskAudience)Audience).ToString(), DefaultTeamId, DefaultTeamName, IsActive, categories, AutoCloseHours);
    }
}
