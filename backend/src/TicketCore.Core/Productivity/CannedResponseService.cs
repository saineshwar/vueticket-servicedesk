using TicketCore.Common;
using TicketCore.Core.Access;
using TicketCore.Data.Organization.Queries;
using TicketCore.Data.Productivity;
using TicketCore.ViewModels.Productivity;

namespace TicketCore.Core.Productivity;

public interface ICannedResponseService
{
    Task<IReadOnlyList<CannedResponseDto>> ListAsync(CancellationToken ct = default);
    Task<OperationResult<CannedResponseDto>> CreateAsync(SaveCannedResponseRequest request, CancellationToken ct = default);
    Task<OperationResult<CannedResponseDto>> UpdateAsync(int id, SaveCannedResponseRequest request, CancellationToken ct = default);
    Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default);
}

/// <summary>
/// Personal saved replies belong to their owner. Shared ones are read by anyone with ticket.reply on the team
/// and managed by people with kb.edit on the team (team leads by default).
/// </summary>
public sealed class CannedResponseService(
    ICannedResponseStore store,
    IOrgUnitQueries orgUnits,
    IPermissionEvaluator permissions,
    ICurrentUser currentUser,
    TimeProvider clock) : ICannedResponseService
{
    public async Task<IReadOnlyList<CannedResponseDto>> ListAsync(CancellationToken ct = default)
    {
        var actor = Actor();
        var teams = await TeamsWhereAsync(actor, Permissions.TicketReply, ct);
        var rows = await store.ListAsync(actor, teams, ct);
        var result = new List<CannedResponseDto>();
        foreach (var r in rows) result.Add(await ToDtoAsync(r, actor, ct));
        return result;
    }

    public async Task<OperationResult<CannedResponseDto>> CreateAsync(SaveCannedResponseRequest request, CancellationToken ct = default)
    {
        var actor = Actor();
        var (title, body, error) = Validate(request);
        if (error is not null) return error;
        if (request.TeamId is { } teamId && !await permissions.CanAsync(actor, Permissions.KbEdit, teamId, ct))
            return Fail(ErrorKind.Forbidden, "forbidden", "You cannot share saved replies with this team.");
        if (request.TeamId is null && !(await permissions.GetAccessAsync(actor, ct)).HasAnywhere(Permissions.TicketReply))
            return Fail(ErrorKind.Forbidden, "forbidden", "Saved replies are for support staff.");

        var id = await store.CreateAsync(title, body, request.TeamId is null ? actor : null, request.TeamId, actor, Now(), ct);
        return OperationResult<CannedResponseDto>.Ok(await ToDtoAsync((await store.GetAsync(id, ct))!, actor, ct));
    }

    public async Task<OperationResult<CannedResponseDto>> UpdateAsync(int id, SaveCannedResponseRequest request, CancellationToken ct = default)
    {
        var actor = Actor();
        var existing = await store.GetAsync(id, ct);
        if (existing is null || !await CanSeeAsync(existing, actor, ct)) return Fail(ErrorKind.NotFound, "not_found", "This saved reply does not exist.");
        if (!await CanEditAsync(existing, actor, ct)) return Fail(ErrorKind.Forbidden, "forbidden", "You cannot change this saved reply.");

        var (title, body, error) = Validate(request);
        if (error is not null) return error;
        await store.UpdateAsync(id, title, body, Now(), ct);
        return OperationResult<CannedResponseDto>.Ok(await ToDtoAsync((await store.GetAsync(id, ct))!, actor, ct));
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var actor = Actor();
        var existing = await store.GetAsync(id, ct);
        if (existing is null || !await CanSeeAsync(existing, actor, ct)) return OperationResult.Fail(ErrorKind.NotFound, "not_found", "This saved reply does not exist.");
        if (!await CanEditAsync(existing, actor, ct)) return OperationResult.Fail(ErrorKind.Forbidden, "forbidden", "You cannot delete this saved reply.");
        await store.DeleteAsync(id, ct);
        return OperationResult.Ok();
    }

    private async Task<IReadOnlyCollection<int>> TeamsWhereAsync(long actor, string permission, CancellationToken ct)
    {
        var teams = (await orgUnits.GetTreeAsync(ct)).Where(u => u.UnitType == nameof(OrgUnitType.Team));
        var result = new List<int>();
        foreach (var team in teams)
            if (await permissions.CanAsync(actor, permission, team.OrgUnitId, ct)) result.Add(team.OrgUnitId);
        return result;
    }

    private async Task<bool> CanSeeAsync(CannedResponseRecord r, long actor, CancellationToken ct) =>
        r.OwnerUserId == actor || (r.TeamId is { } t && await permissions.CanAsync(actor, Permissions.TicketReply, t, ct));

    private async Task<bool> CanEditAsync(CannedResponseRecord r, long actor, CancellationToken ct) =>
        r.OwnerUserId == actor || (r.TeamId is { } t && await permissions.CanAsync(actor, Permissions.KbEdit, t, ct));

    private async Task<CannedResponseDto> ToDtoAsync(CannedResponseRecord r, long actor, CancellationToken ct) =>
        new(r.CannedResponseId, r.Title, r.Body, r.TeamId, r.TeamName, r.OwnerUserId is not null, await CanEditAsync(r, actor, ct));

    private static (string Title, string Body, OperationResult<CannedResponseDto>? Error) Validate(SaveCannedResponseRequest request)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var body = request.Body?.Trim() ?? string.Empty;
        if (title.Length is 0 or > 100) return (title, body, Fail(ErrorKind.Validation, "invalid_title", "Enter a title of up to 100 characters."));
        if (body.Length is 0 or > 4000) return (title, body, Fail(ErrorKind.Validation, "invalid_body", "Write the reply text (up to 4,000 characters)."));
        return (title, body, null);
    }

    private long Actor() => currentUser.UserId ?? throw new InvalidOperationException("Saved replies require a signed-in user.");
    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static OperationResult<CannedResponseDto> Fail(ErrorKind kind, string code, string message) =>
        OperationResult<CannedResponseDto>.Fail(kind, code, message);
}
