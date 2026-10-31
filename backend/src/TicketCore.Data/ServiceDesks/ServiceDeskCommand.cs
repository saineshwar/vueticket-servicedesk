using Dapper;
using TicketCore.Common;

namespace TicketCore.Data.ServiceDesks.Commands;

public interface IServiceDeskCommand
{
    Task<int> CreateAsync(string name, string code, string? description, DeskAudience audience, int defaultTeamId, bool isActive, int autoCloseHours, CancellationToken ct = default);
    Task UpdateAsync(int serviceDeskId, string name, string code, string? description, DeskAudience audience, int defaultTeamId, bool isActive, int autoCloseHours, CancellationToken ct = default);
    Task<int> CreateCategoryAsync(int serviceDeskId, CategoryValues values, CancellationToken ct = default);
    Task UpdateCategoryAsync(int categoryId, CategoryValues values, CancellationToken ct = default);
}

public sealed record CategoryValues(string Name, string? Description, int? TeamId, byte TicketTypeId, byte DefaultPriorityId, int SortOrder, bool IsActive);

public sealed class ServiceDeskCommand(ISqlConnectionFactory connections) : IServiceDeskCommand
{
    public async Task<int> CreateAsync(string name, string code, string? description, DeskAudience audience, int defaultTeamId, bool isActive, int autoCloseHours, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO dbo.ServiceDesk (Name, Code, Description, Audience, DefaultTeamId, IsActive, AutoCloseHours)
            OUTPUT inserted.ServiceDeskId
            VALUES (@name, @code, @description, @audience, @defaultTeamId, @isActive, @autoCloseHours)
            """, new { name, code, description, audience = (byte)audience, defaultTeamId, isActive, autoCloseHours }, cancellationToken: ct));
    }

    public async Task UpdateAsync(int serviceDeskId, string name, string code, string? description, DeskAudience audience, int defaultTeamId, bool isActive, int autoCloseHours, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.ServiceDesk
            SET Name = @name, Code = @code, Description = @description, Audience = @audience,
                DefaultTeamId = @defaultTeamId, IsActive = @isActive, AutoCloseHours = @autoCloseHours, UpdatedAt = SYSUTCDATETIME()
            WHERE ServiceDeskId = @serviceDeskId
            """, new { serviceDeskId, name, code, description, audience = (byte)audience, defaultTeamId, isActive, autoCloseHours }, cancellationToken: ct));
    }

    public async Task<int> CreateCategoryAsync(int serviceDeskId, CategoryValues v, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        return await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO dbo.Category (ServiceDeskId, Name, Description, TeamId, TicketTypeId, DefaultPriorityId, SortOrder, IsActive)
            OUTPUT inserted.CategoryId
            VALUES (@serviceDeskId, @Name, @Description, @TeamId, @TicketTypeId, @DefaultPriorityId, @SortOrder, @IsActive)
            """, new { serviceDeskId, v.Name, v.Description, v.TeamId, v.TicketTypeId, v.DefaultPriorityId, v.SortOrder, v.IsActive }, cancellationToken: ct));
    }

    public async Task UpdateCategoryAsync(int categoryId, CategoryValues v, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.Category
            SET Name = @Name, Description = @Description, TeamId = @TeamId, TicketTypeId = @TicketTypeId,
                DefaultPriorityId = @DefaultPriorityId, SortOrder = @SortOrder, IsActive = @IsActive
            WHERE CategoryId = @categoryId
            """, new { categoryId, v.Name, v.Description, v.TeamId, v.TicketTypeId, v.DefaultPriorityId, v.SortOrder, v.IsActive }, cancellationToken: ct));
    }
}
