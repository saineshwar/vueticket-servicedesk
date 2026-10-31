using System.Text.RegularExpressions;
using TicketCore.Common;
using TicketCore.Core.Access;
using TicketCore.Data.Audit;
using TicketCore.Data.Organization.Queries;
using TicketCore.Data.ServiceDesks.Commands;
using TicketCore.Data.ServiceDesks.Queries;
using TicketCore.ViewModels.ServiceDesks;

namespace TicketCore.Core.ServiceDesks;

public interface IServiceDeskService
{
    Task<OperationResult<ServiceDeskDto>> CreateAsync(SaveServiceDeskRequest request, CancellationToken ct = default);
    Task<OperationResult<ServiceDeskDto>> UpdateAsync(int serviceDeskId, SaveServiceDeskRequest request, CancellationToken ct = default);
    Task<OperationResult<ServiceDeskDto>> CreateCategoryAsync(int serviceDeskId, SaveCategoryRequest request, CancellationToken ct = default);
    Task<OperationResult<ServiceDeskDto>> UpdateCategoryAsync(int categoryId, SaveCategoryRequest request, CancellationToken ct = default);
}

/// <summary>Desk configuration needs config.desks on the desk's default team (so desk admins stay in their own area).</summary>
public sealed partial class ServiceDeskService(
    IServiceDeskQueries queries,
    IServiceDeskCommand command,
    IOrgUnitQueries orgUnits,
    IPermissionEvaluator permissions,
    IAuditCommand audit,
    ICurrentUser currentUser) : IServiceDeskService
{
    private const int DefaultAutoCloseHours = 48;
    private const int MaxAutoCloseHours = 720;

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,9}$")]
    private static partial Regex DeskCodePattern();

    public async Task<OperationResult<ServiceDeskDto>> CreateAsync(SaveServiceDeskRequest request, CancellationToken ct = default)
    {
        var (values, error) = await ValidateDeskAsync(request, null, ct);
        if (error is not null) return error;
        if (!await CanConfigureAsync(request.DefaultTeamId, ct)) return Forbidden();

        var id = await command.CreateAsync(values!.Name, values.Code, values.Description, values.Audience, request.DefaultTeamId, request.IsActive,
            request.AutoCloseHours ?? DefaultAutoCloseHours, ct);
        await audit.WriteAsync(currentUser.UserId, "desk.created", "ServiceDesk", id.ToString(), request, currentUser.IpAddress, ct);
        return OperationResult<ServiceDeskDto>.Ok((await queries.GetAsync(id, ct))!);
    }

    public async Task<OperationResult<ServiceDeskDto>> UpdateAsync(int serviceDeskId, SaveServiceDeskRequest request, CancellationToken ct = default)
    {
        var existing = await queries.GetAsync(serviceDeskId, ct);
        if (existing is null) return Fail(ErrorKind.NotFound, "not_found", "This service desk does not exist.");
        if (!await CanConfigureAsync(existing.DefaultTeamId, ct) || !await CanConfigureAsync(request.DefaultTeamId, ct)) return Forbidden();

        var (values, error) = await ValidateDeskAsync(request, serviceDeskId, ct);
        if (error is not null) return error;

        await command.UpdateAsync(serviceDeskId, values!.Name, values.Code, values.Description, values.Audience, request.DefaultTeamId, request.IsActive,
            request.AutoCloseHours ?? existing.AutoCloseHours, ct);
        await audit.WriteAsync(currentUser.UserId, "desk.updated", "ServiceDesk", serviceDeskId.ToString(), request, currentUser.IpAddress, ct);
        return OperationResult<ServiceDeskDto>.Ok((await queries.GetAsync(serviceDeskId, ct))!);
    }

    public async Task<OperationResult<ServiceDeskDto>> CreateCategoryAsync(int serviceDeskId, SaveCategoryRequest request, CancellationToken ct = default)
    {
        var desk = await queries.GetAsync(serviceDeskId, ct);
        if (desk is null) return Fail(ErrorKind.NotFound, "not_found", "This service desk does not exist.");
        if (!await CanConfigureAsync(desk.DefaultTeamId, ct)) return Forbidden();

        var (values, error) = await ValidateCategoryAsync(serviceDeskId, request, null, ct);
        if (error is not null) return error;

        var id = await command.CreateCategoryAsync(serviceDeskId, values!, ct);
        await audit.WriteAsync(currentUser.UserId, "category.created", "Category", id.ToString(), request, currentUser.IpAddress, ct);
        return OperationResult<ServiceDeskDto>.Ok((await queries.GetAsync(serviceDeskId, ct))!);
    }

    public async Task<OperationResult<ServiceDeskDto>> UpdateCategoryAsync(int categoryId, SaveCategoryRequest request, CancellationToken ct = default)
    {
        var category = await queries.GetCategoryAsync(categoryId, ct);
        if (category is null) return Fail(ErrorKind.NotFound, "not_found", "This category does not exist.");
        var desk = (await queries.GetAsync(category.ServiceDeskId, ct))!;
        if (!await CanConfigureAsync(desk.DefaultTeamId, ct)) return Forbidden();

        var (values, error) = await ValidateCategoryAsync(category.ServiceDeskId, request, categoryId, ct);
        if (error is not null) return error;

        await command.UpdateCategoryAsync(categoryId, values!, ct);
        await audit.WriteAsync(currentUser.UserId, "category.updated", "Category", categoryId.ToString(), request, currentUser.IpAddress, ct);
        return OperationResult<ServiceDeskDto>.Ok((await queries.GetAsync(category.ServiceDeskId, ct))!);
    }

    private sealed record DeskValues(string Name, string Code, string? Description, DeskAudience Audience);

    private async Task<(DeskValues? Values, OperationResult<ServiceDeskDto>? Error)> ValidateDeskAsync(
        SaveServiceDeskRequest request, int? excludeId, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (name.Length is 0 or > 100) return (null, Fail(ErrorKind.Validation, "invalid_name", "Enter a desk name of up to 100 characters."));
        if (!DeskCodePattern().IsMatch(code))
            return (null, Fail(ErrorKind.Validation, "invalid_code", "Use a code of 2 to 10 capital letters or digits, starting with a letter, for example IT. It prefixes ticket numbers."));
        if (!Enum.TryParse<DeskAudience>(request.Audience, true, out var audience))
            return (null, Fail(ErrorKind.Validation, "invalid_audience", "Choose Employees, Customers or Both."));
        if (request.AutoCloseHours is < 0 or > MaxAutoCloseHours)
            return (null, Fail(ErrorKind.Validation, "invalid_auto_close", "Auto-close must be between 0 (never) and 720 hours (30 days)."));

        var teamError = await ValidateTeamAsync(request.DefaultTeamId, "default team", ct);
        if (teamError is not null) return (null, teamError);

        if (await queries.CodeOrNameExistsAsync(code, name, excludeId, ct))
            return (null, Fail(ErrorKind.Conflict, "desk_exists", "Another desk already uses this name or code."));

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        return (new DeskValues(name, code, description, audience), null);
    }

    private async Task<(CategoryValues? Values, OperationResult<ServiceDeskDto>? Error)> ValidateCategoryAsync(
        int serviceDeskId, SaveCategoryRequest request, int? excludeCategoryId, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100) return (null, Fail(ErrorKind.Validation, "invalid_name", "Enter a category name of up to 100 characters."));

        var lookups = await queries.GetLookupsAsync(ct);
        if (lookups.TicketTypes.All(t => t.Id != request.TicketTypeId))
            return (null, Fail(ErrorKind.Validation, "invalid_type", "Choose a ticket type."));
        if (lookups.Priorities.All(p => p.Id != request.DefaultPriorityId))
            return (null, Fail(ErrorKind.Validation, "invalid_priority", "Choose a default priority."));

        if (request.TeamId is { } teamId)
        {
            var teamError = await ValidateTeamAsync(teamId, "team", ct);
            if (teamError is not null) return (null, teamError);
        }

        if (await queries.CategoryNameExistsAsync(serviceDeskId, name, excludeCategoryId, ct))
            return (null, Fail(ErrorKind.Conflict, "category_exists", $"This desk already has a category called {name}."));

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        return (new CategoryValues(name, description, request.TeamId, (byte)request.TicketTypeId, (byte)request.DefaultPriorityId,
            request.SortOrder, request.IsActive), null);
    }

    /// <summary>Tickets belong to teams, so routing targets must be active teams.</summary>
    private async Task<OperationResult<ServiceDeskDto>?> ValidateTeamAsync(int teamId, string label, CancellationToken ct)
    {
        var unit = await orgUnits.GetByIdAsync(teamId, ct);
        if (unit is null || unit.UnitType != nameof(OrgUnitType.Team))
            return Fail(ErrorKind.Validation, "invalid_team", $"Choose a team as the {label}. Tickets are worked by teams.");
        if (!unit.IsActive) return Fail(ErrorKind.Validation, "inactive_team", $"{unit.Name} is inactive. Choose an active team.");
        return null;
    }

    private Task<bool> CanConfigureAsync(int teamId, CancellationToken ct) =>
        currentUser.UserId is { } actor ? permissions.CanAsync(actor, Permissions.ConfigDesks, teamId, ct) : Task.FromResult(false);

    private static OperationResult<ServiceDeskDto> Forbidden() =>
        Fail(ErrorKind.Forbidden, "forbidden", "You cannot configure service desks for this team.");

    private static OperationResult<ServiceDeskDto> Fail(ErrorKind kind, string code, string message) =>
        OperationResult<ServiceDeskDto>.Fail(kind, code, message);
}
