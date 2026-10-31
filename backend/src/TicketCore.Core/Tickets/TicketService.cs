using Microsoft.Extensions.Options;
using TicketCore.Common;
using TicketCore.Core.Access;
using TicketCore.Core.Notifications;
using TicketCore.Data.Organization.Queries;
using TicketCore.Data.ServiceDesks.Queries;
using TicketCore.Data.Tickets.Commands;
using TicketCore.Data.Tickets.Queries;
using TicketCore.Data.Users.Queries;
using TicketCore.Services.Storage;
using TicketCore.ViewModels.Sla;
using TicketCore.ViewModels.Tickets;
using TicketCore.ViewModels.Users;

namespace TicketCore.Core.Tickets;

public interface ITicketService
{
    Task<OperationResult<TicketDetailDto>> CreateAsync(CreateTicketRequest request, IReadOnlyList<IncomingFile> files, CancellationToken ct = default);
    Task<PagedResult<TicketListItemDto>> ListAsync(TicketListQuery query, CancellationToken ct = default);
    Task<OperationResult<TicketDetailDto>> GetAsync(long ticketId, CancellationToken ct = default);
    Task<OperationResult<TicketDetailDto>> AddCommentAsync(long ticketId, AddCommentRequest request, IReadOnlyList<IncomingFile> files, CancellationToken ct = default);
    Task<OperationResult<TicketDetailDto>> AddAttachmentsAsync(long ticketId, IReadOnlyList<IncomingFile> files, CancellationToken ct = default);
    Task<OperationResult<TicketDetailDto>> ChangeStatusAsync(long ticketId, ChangeStatusRequest request, CancellationToken ct = default);
    Task<OperationResult<TicketDetailDto>> ChangePriorityAsync(long ticketId, ChangePriorityRequest request, CancellationToken ct = default);
    Task<OperationResult<TicketDetailDto>> AssignAsync(long ticketId, AssignTicketRequest request, CancellationToken ct = default);
    Task<OperationResult<TicketDetailDto>> TransferAsync(long ticketId, TransferTicketRequest request, CancellationToken ct = default);
    Task<OperationResult<TicketDetailDto>> DeleteAsync(long ticketId, DeleteTicketRequest request, CancellationToken ct = default);
    Task<OperationResult<TicketDetailDto>> RestoreAsync(long ticketId, CancellationToken ct = default);
    Task<OperationResult<IReadOnlyList<AssignableUserDto>>> GetAssignableUsersAsync(long ticketId, CancellationToken ct = default);
    Task<OperationResult<(AttachmentRecord Attachment, Stream Content)>> OpenAttachmentAsync(long attachmentId, CancellationToken ct = default);
}

/// <summary>
/// Who may see a ticket: its requester, its assignee, and anyone with ticket.view on its team.
/// Deleted tickets are visible only to people with ticket.restore on the team. Anyone else gets "not found",
/// so ticket numbers cannot be probed.
/// </summary>
public sealed class TicketService(
    ITicketQueries queries,
    ITicketCommand command,
    IServiceDeskQueries desks,
    IUserQueries users,
    IOrgUnitQueries orgUnits,
    IPermissionEvaluator permissions,
    IFileStorage storage,
    IOptions<StorageOptions> storageOptions,
    ITicketNotifier notifier,
    ICurrentUser currentUser,
    TimeProvider clock) : ITicketService
{
    private const int MaxSubject = 200;
    private const int MaxText = 20_000;

    public async Task<OperationResult<TicketDetailDto>> CreateAsync(CreateTicketRequest request, IReadOnlyList<IncomingFile> files, CancellationToken ct = default)
    {
        var actorId = RequireActor();
        var subject = request.Subject?.Trim() ?? string.Empty;
        var description = request.Description?.Trim() ?? string.Empty;
        if (subject.Length is 0 or > MaxSubject) return Fail(ErrorKind.Validation, "invalid_subject", "Enter a subject of up to 200 characters.");
        if (description.Length is 0 or > MaxText) return Fail(ErrorKind.Validation, "invalid_description", "Describe the request (up to 20,000 characters).");

        var desk = await desks.GetAsync(request.ServiceDeskId, ct);
        if (desk is null || !desk.IsActive) return Fail(ErrorKind.Validation, "desk_unavailable", "This service desk is not available.");
        var category = desk.Categories.FirstOrDefault(c => c.CategoryId == request.CategoryId && c.IsActive);
        if (category is null) return Fail(ErrorKind.Validation, "invalid_category", "Choose a category from this desk.");

        var teamId = category.TeamId ?? desk.DefaultTeamId;

        var onBehalf = request.RequesterUserId is { } rid && rid != actorId;
        var requesterId = request.RequesterUserId ?? actorId;
        if (onBehalf && !await permissions.CanAsync(actorId, Permissions.TicketCreateOnBehalf, teamId, ct))
            return Fail(ErrorKind.Forbidden, "forbidden", "You cannot raise tickets for other people on this desk.");

        var requester = await users.GetByIdAsync(requesterId, ct);
        if (requester is null || !requester.IsActive) return Fail(ErrorKind.Validation, "invalid_requester", "Choose an active requester.");
        if (!TicketRules.AudienceAllows(Enum.Parse<DeskAudience>(desk.Audience), requester.UserType))
            return Fail(ErrorKind.Validation, "wrong_desk", $"{desk.Name} is not available to {requester.UserType.ToString().ToLowerInvariant()}s.");

        var priorityId = request.PriorityId ?? category.DefaultPriorityId;
        if (priorityId is < 1 or > 4) return Fail(ErrorKind.Validation, "invalid_priority", "Choose a priority.");

        // Store files first so a bad file rejects the whole request before anything is created.
        var (stored, fileError) = await StoreFilesAsync(files, ct);
        if (fileError is not null) return fileError;

        long ticketId;
        try
        {
            (ticketId, _) = await command.CreateAsync(new NewTicket(
                desk.ServiceDeskId, desk.Code, category.CategoryId, (byte)category.TicketTypeId, teamId, subject, description,
                (byte)priorityId, requesterId, actorId, onBehalf ? TicketChannel.Agent : TicketChannel.Portal, Now()), ct);

            if (stored.Count > 0)
            {
                var change = new TicketChange(ticketId, actorId, Now());
                change.Attachments.AddRange(stored);
                change.History.Add(new HistoryEntry(TicketActivities.Attached, null, FileList(stored)));
                await command.ApplyAsync(change, ct);
            }
        }
        catch
        {
            await DeleteStoredAsync(stored);
            throw;
        }

        await NotifyAsync(TicketEvent.Created, ticketId, actorId, null, ct);
        return await GetAsync(ticketId, ct);
    }

    public async Task<PagedResult<TicketListItemDto>> ListAsync(TicketListQuery query, CancellationToken ct = default) =>
        // Team, Unassigned and Deleted views are filtered by scope in SQL; no grants means an empty list.
        await queries.ListAsync(RequireActor(), query, ct);

    public async Task<OperationResult<TicketDetailDto>> GetAsync(long ticketId, CancellationToken ct = default)
    {
        var (ticket, viewer) = await LoadAsync(ticketId, ct);
        if (ticket is null) return NotFound();
        return OperationResult<TicketDetailDto>.Ok(await ToDetailAsync(ticket, viewer!, ct));
    }

    public async Task<OperationResult<TicketDetailDto>> AddCommentAsync(long ticketId, AddCommentRequest request, IReadOnlyList<IncomingFile> files, CancellationToken ct = default)
    {
        var (ticket, viewer) = await LoadAsync(ticketId, ct);
        if (ticket is null) return NotFound();
        if (ticket.IsDeleted) return Fail(ErrorKind.Validation, "ticket_deleted", "Restore this ticket before working on it.");

        var body = request.Body?.Trim() ?? string.Empty;
        if (body.Length is 0 or > MaxText) return Fail(ErrorKind.Validation, "invalid_body", "Write a message (up to 20,000 characters).");

        var change = new TicketChange(ticket.TicketId, viewer!.UserId, Now());
        TicketEvent? evt;

        if (viewer.IsSupport)
        {
            if (request.IsInternal && !viewer.CanNote) return Fail(ErrorKind.Forbidden, "forbidden", "You cannot add internal notes here.");
            if (!request.IsInternal && !viewer.CanReply) return Fail(ErrorKind.Forbidden, "forbidden", "You cannot reply on this ticket.");

            change.Comment = new NewComment(body, request.IsInternal, AuthorIsSupport: true);
            change.History.Add(new HistoryEntry(request.IsInternal ? TicketActivities.NoteAdded : TicketActivities.Replied, null, null));

            TicketStatus? target = null;
            if (!string.IsNullOrEmpty(request.NewStatus))
            {
                if (!Enum.TryParse<TicketStatus>(request.NewStatus, true, out var chosen) || !viewer.CanChangeStatus
                    || !TicketRules.CanStaffSet(ticket.Status, chosen, viewer.CanReopenClosed))
                    return Fail(ErrorKind.Validation, "invalid_status", "You cannot set that status on this ticket.");
                target = chosen;
            }
            else if (!request.IsInternal)
            {
                target = TicketRules.AfterSupportReply(ticket.Status);
            }

            if (!request.IsInternal) change.MarkFirstResponse = true;
            if (target is { } t) ApplyStatus(change, ticket.Status, t);
            evt = request.IsInternal ? null : TicketEvent.SupportReplied;
        }
        else
        {
            if (request.IsInternal) return Fail(ErrorKind.Forbidden, "forbidden", "Only support staff can add internal notes.");
            if (!TicketRules.RequesterCanReply(ticket.Status))
                return Fail(ErrorKind.Validation, "ticket_closed", "This request is closed. Create a new request if you still need help.");

            change.Comment = new NewComment(body, IsInternal: false, AuthorIsSupport: false);
            change.History.Add(new HistoryEntry(TicketActivities.RequesterReplied, null, null));
            if (TicketRules.AfterRequesterReply(ticket.Status) is { } t)
            {
                if (ticket.Status == TicketStatus.Resolved) change.History.Add(new HistoryEntry(TicketActivities.Reopened, null, null));
                ApplyStatus(change, ticket.Status, t);
            }
            evt = TicketEvent.RequesterReplied;
        }

        var (stored, fileError) = await StoreFilesAsync(files, ct);
        if (fileError is not null) return fileError;
        change.Attachments.AddRange(stored);

        try
        {
            await command.ApplyAsync(change, ct);
        }
        catch
        {
            await DeleteStoredAsync(stored);
            throw;
        }

        if (evt is { } e) await NotifyAsync(e, ticketId, viewer.UserId, request.IsInternal ? null : body, ct);
        if (change.Status == TicketStatus.Resolved) await NotifyAsync(TicketEvent.Resolved, ticketId, viewer.UserId, null, ct);
        return await GetAsync(ticketId, ct);
    }

    public async Task<OperationResult<TicketDetailDto>> AddAttachmentsAsync(long ticketId, IReadOnlyList<IncomingFile> files, CancellationToken ct = default)
    {
        var (ticket, viewer) = await LoadAsync(ticketId, ct);
        if (ticket is null) return NotFound();
        if (ticket.IsDeleted) return Fail(ErrorKind.Validation, "ticket_deleted", "Restore this ticket before working on it.");
        var allowed = viewer!.IsSupport ? viewer.CanReply || viewer.CanNote : TicketRules.RequesterCanReply(ticket.Status);
        if (!allowed) return Fail(ErrorKind.Forbidden, "forbidden", "You cannot add files to this ticket.");
        if (files.Count == 0) return Fail(ErrorKind.Validation, "no_files", "Choose at least one file.");

        var (stored, fileError) = await StoreFilesAsync(files, ct);
        if (fileError is not null) return fileError;

        var change = new TicketChange(ticket.TicketId, viewer.UserId, Now());
        change.Attachments.AddRange(stored);
        change.History.Add(new HistoryEntry(TicketActivities.Attached, null, FileList(stored)));
        try
        {
            await command.ApplyAsync(change, ct);
        }
        catch
        {
            await DeleteStoredAsync(stored);
            throw;
        }
        return await GetAsync(ticketId, ct);
    }

    public async Task<OperationResult<TicketDetailDto>> ChangeStatusAsync(long ticketId, ChangeStatusRequest request, CancellationToken ct = default)
    {
        var (ticket, viewer) = await LoadAsync(ticketId, ct);
        if (ticket is null) return NotFound();
        if (!viewer!.CanChangeStatus) return Fail(ErrorKind.Forbidden, "forbidden", "You cannot change the status of this ticket.");
        if (!Enum.TryParse<TicketStatus>(request.Status, true, out var target) || !TicketRules.CanStaffSet(ticket.Status, target, viewer.CanReopenClosed))
            return Fail(ErrorKind.Validation, "invalid_status", $"A {Describe(ticket.Status)} ticket cannot be set to that status.");

        var change = new TicketChange(ticket.TicketId, viewer.UserId, Now());
        ApplyStatus(change, ticket.Status, target);
        await command.ApplyAsync(change, ct);
        if (target == TicketStatus.Resolved) await NotifyAsync(TicketEvent.Resolved, ticketId, viewer.UserId, null, ct);
        return await GetAsync(ticketId, ct);
    }

    public async Task<OperationResult<TicketDetailDto>> ChangePriorityAsync(long ticketId, ChangePriorityRequest request, CancellationToken ct = default)
    {
        var (ticket, viewer) = await LoadAsync(ticketId, ct);
        if (ticket is null) return NotFound();
        if (!viewer!.CanChangePriority) return Fail(ErrorKind.Forbidden, "forbidden", "You cannot change the priority of this ticket.");

        var priorities = (await desks.GetLookupsAsync(ct)).Priorities;
        var target = priorities.FirstOrDefault(p => p.Id == request.PriorityId);
        if (target is null) return Fail(ErrorKind.Validation, "invalid_priority", "Choose a priority.");
        if (target.Id == ticket.PriorityId) return await GetAsync(ticketId, ct);

        var change = new TicketChange(ticket.TicketId, viewer.UserId, Now()) { PriorityId = (byte)target.Id };
        change.History.Add(new HistoryEntry(TicketActivities.PriorityChanged, ticket.PriorityName, target.Name));
        await command.ApplyAsync(change, ct);
        return await GetAsync(ticketId, ct);
    }

    public async Task<OperationResult<TicketDetailDto>> AssignAsync(long ticketId, AssignTicketRequest request, CancellationToken ct = default)
    {
        var (ticket, viewer) = await LoadAsync(ticketId, ct);
        if (ticket is null) return NotFound();
        if (!viewer!.CanAssign) return Fail(ErrorKind.Forbidden, "forbidden", "You cannot assign this ticket.");
        if (ticket.AssigneeUserId == request.AssigneeUserId) return await GetAsync(ticketId, ct);

        string? newName = null;
        if (request.AssigneeUserId is { } assigneeId)
        {
            var candidate = (await queries.GetAssignableUsersAsync(ticket.TeamId, ct)).FirstOrDefault(u => u.UserId == assigneeId);
            if (candidate is null)
                return Fail(ErrorKind.Validation, "not_assignable", $"That person cannot work tickets in {ticket.TeamName}.");
            newName = candidate.DisplayName;
        }

        var change = new TicketChange(ticket.TicketId, viewer.UserId, Now()) { ChangeAssignee = true, AssigneeUserId = request.AssigneeUserId };
        change.History.Add(new HistoryEntry(TicketActivities.Assigned, ticket.AssigneeName, newName));
        if (request.AssigneeUserId is not null && ticket.Status == TicketStatus.New) ApplyStatus(change, ticket.Status, TicketStatus.Open);
        if (request.AssigneeUserId is null && ticket.Status == TicketStatus.Open) ApplyStatus(change, ticket.Status, TicketStatus.New);

        await command.ApplyAsync(change, ct);
        if (request.AssigneeUserId is not null) await NotifyAsync(TicketEvent.Assigned, ticketId, viewer.UserId, null, ct);
        return await GetAsync(ticketId, ct);
    }

    public async Task<OperationResult<TicketDetailDto>> TransferAsync(long ticketId, TransferTicketRequest request, CancellationToken ct = default)
    {
        var (ticket, viewer) = await LoadAsync(ticketId, ct);
        if (ticket is null) return NotFound();
        if (!viewer!.CanTransfer) return Fail(ErrorKind.Forbidden, "forbidden", "You cannot transfer this ticket.");
        if (request.TeamId == ticket.TeamId) return Fail(ErrorKind.Validation, "same_team", $"This ticket is already with {ticket.TeamName}.");

        var team = await orgUnits.GetByIdAsync(request.TeamId, ct);
        if (team is null || team.UnitType != nameof(OrgUnitType.Team) || !team.IsActive)
            return Fail(ErrorKind.Validation, "invalid_team", "Choose an active team.");

        // A new team decides who works it, so the assignee is cleared.
        var change = new TicketChange(ticket.TicketId, viewer.UserId, Now())
        {
            TeamId = team.OrgUnitId,
            ChangeAssignee = true,
            AssigneeUserId = null
        };
        change.History.Add(new HistoryEntry(TicketActivities.Transferred, ticket.TeamName, team.Name));
        if (ticket.Status == TicketStatus.Open) ApplyStatus(change, ticket.Status, TicketStatus.New);
        await command.ApplyAsync(change, ct);

        // After a transfer the caller may no longer see the ticket; return it only if they still can.
        var after = await GetAsync(ticketId, ct);
        return after.Succeeded ? after : OperationResult<TicketDetailDto>.Fail(ErrorKind.NotFound, "transferred_away",
            $"Transferred to {team.Name}. You no longer have access to this ticket.");
    }

    public async Task<OperationResult<TicketDetailDto>> DeleteAsync(long ticketId, DeleteTicketRequest request, CancellationToken ct = default)
    {
        var (ticket, viewer) = await LoadAsync(ticketId, ct);
        if (ticket is null) return NotFound();
        if (ticket.IsDeleted) return await GetAsync(ticketId, ct);
        if (!viewer!.CanDelete) return Fail(ErrorKind.Forbidden, "forbidden", "You cannot delete this ticket.");

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 500) return Fail(ErrorKind.Validation, "invalid_reason", "Say why you are deleting it (up to 500 characters).");

        var change = new TicketChange(ticket.TicketId, viewer.UserId, Now()) { DeleteReason = reason };
        change.History.Add(new HistoryEntry(TicketActivities.Deleted, null, reason));
        await command.ApplyAsync(change, ct);
        return await GetAsync(ticketId, ct);
    }

    public async Task<OperationResult<TicketDetailDto>> RestoreAsync(long ticketId, CancellationToken ct = default)
    {
        var (ticket, viewer) = await LoadAsync(ticketId, ct);
        if (ticket is null) return NotFound();
        if (!ticket.IsDeleted) return await GetAsync(ticketId, ct);
        if (!viewer!.CanRestore) return Fail(ErrorKind.Forbidden, "forbidden", "You cannot restore this ticket.");

        var change = new TicketChange(ticket.TicketId, viewer.UserId, Now()) { Restore = true };
        change.History.Add(new HistoryEntry(TicketActivities.Restored, null, null));
        await command.ApplyAsync(change, ct);
        return await GetAsync(ticketId, ct);
    }

    public async Task<OperationResult<IReadOnlyList<AssignableUserDto>>> GetAssignableUsersAsync(long ticketId, CancellationToken ct = default)
    {
        var (ticket, viewer) = await LoadAsync(ticketId, ct);
        if (ticket is null)
            return OperationResult<IReadOnlyList<AssignableUserDto>>.Fail(ErrorKind.NotFound, "not_found", "This ticket does not exist.");
        if (!viewer!.CanAssign)
            return OperationResult<IReadOnlyList<AssignableUserDto>>.Fail(ErrorKind.Forbidden, "forbidden", "You cannot assign this ticket.");
        return OperationResult<IReadOnlyList<AssignableUserDto>>.Ok(await queries.GetAssignableUsersAsync(ticket.TeamId, ct));
    }

    public async Task<OperationResult<(AttachmentRecord Attachment, Stream Content)>> OpenAttachmentAsync(long attachmentId, CancellationToken ct = default)
    {
        static OperationResult<(AttachmentRecord, Stream)> Missing() =>
            OperationResult<(AttachmentRecord, Stream)>.Fail(ErrorKind.NotFound, "not_found", "This file does not exist or you do not have access to it.");

        var attachment = await queries.GetAttachmentAsync(attachmentId, ct);
        if (attachment is null) return Missing();

        var (ticket, viewer) = await LoadAsync(attachment.TicketId, ct);
        if (ticket is null || (attachment.IsInternal && !viewer!.IsSupport)) return Missing();

        try
        {
            return OperationResult<(AttachmentRecord, Stream)>.Ok((attachment, await storage.OpenReadAsync(attachment.StorageKey, ct)));
        }
        catch (FileNotFoundException)
        {
            return Missing();
        }
        catch (DirectoryNotFoundException)
        {
            return Missing();
        }
    }

    // ---- helpers ----

    private sealed record Viewer(
        long UserId, bool IsRequester, bool IsSupport, bool CanReply, bool CanNote, bool CanChangeStatus,
        bool CanReopenClosed, bool CanChangePriority, bool CanAssign, bool CanTransfer, bool CanDelete, bool CanRestore);

    /// <summary>Loads the ticket and works out what the caller may do. Returns (null, null) when the caller may not see it.</summary>
    private async Task<(TicketRecord? Ticket, Viewer? Viewer)> LoadAsync(long ticketId, CancellationToken ct)
    {
        var actorId = RequireActor();
        var ticket = await queries.GetAsync(ticketId, includeDeleted: true, ct);
        if (ticket is null) return (null, null);

        Task<bool> Can(string key) => permissions.CanAsync(actorId, key, ticket.TeamId, ct);

        if (ticket.IsDeleted)
        {
            // Deleted tickets are only for people who can restore them, and nothing else can be done until they do.
            if (!await Can(Permissions.TicketRestore)) return (null, null);
            return (ticket, new Viewer(actorId, false, true, false, false, false, false, false, false, false, false, CanRestore: true));
        }

        var isRequester = ticket.RequesterUserId == actorId;
        var isAssignee = ticket.AssigneeUserId == actorId;
        var canView = await Can(Permissions.TicketView);
        if (!isRequester && !isAssignee && !canView) return (null, null);

        var isSupport = canView || isAssignee;
        var viewer = new Viewer(
            actorId, isRequester, isSupport,
            CanReply: isSupport && await Can(Permissions.TicketReply),
            CanNote: isSupport && await Can(Permissions.TicketNote),
            CanChangeStatus: isSupport && await Can(Permissions.TicketStatus),
            CanReopenClosed: isSupport && await Can(Permissions.TicketReopenClosed),
            CanChangePriority: isSupport && await Can(Permissions.TicketPriority),
            CanAssign: isSupport && await Can(Permissions.TicketAssign),
            CanTransfer: isSupport && await Can(Permissions.TicketTransfer),
            CanDelete: isSupport && await Can(Permissions.TicketDelete),
            CanRestore: false);
        return (ticket, viewer);
    }

    private async Task<TicketDetailDto> ToDetailAsync(TicketRecord t, Viewer v, CancellationToken ct)
    {
        var comments = await queries.GetCommentsAsync(t.TicketId, includeInternal: v.IsSupport, ct);
        var attachments = await queries.GetAttachmentsAsync(t.TicketId, includeInternal: v.IsSupport, ct);
        var history = await queries.GetHistoryAsync(t.TicketId, ct);
        if (!v.IsSupport)
            history = history.Where(h => h.Activity is not (TicketActivities.NoteAdded or TicketActivities.Deleted or TicketActivities.Restored
                or TicketActivities.SlaBreached or TicketActivities.Escalated or TicketActivities.EscalationCleared)).ToList();

        // SLA targets and escalations are internal to support.
        TicketSlaDto? sla = null;
        TicketEscalationDto? escalation = null;
        if (v.IsSupport && !t.IsDeleted)
        {
            if (await queries.GetSlaAsync(t.TicketId, ct) is { } s)
            {
                List<SlaClockDto> clocks =
                [
                    new("first_response", s.FirstResponseDueAt, s.FirstResponseMetAt, s.FirstResponseBreachedAt),
                    new("resolution", s.ResolutionDueAt, s.ResolutionMetAt, s.ResolutionBreachedAt)
                ];
                // The next-response clock only exists while a requester's reply is waiting for an answer.
                if (s.NextResponseDueAt is not null) clocks.Insert(1, new("next_response", s.NextResponseDueAt, null, s.NextResponseBreachedAt));
                sla = new TicketSlaDto(s.PolicyName, clocks, s.PausedAt);
            }
            if (await queries.GetActiveEscalationAsync(t.TicketId, ct) is { } e)
                escalation = new TicketEscalationDto(e.Level, e.RoleName,
                    e.Reasons.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    e.OwnerUserId, e.OwnerName, e.StartedAt, e.WindowEndsAt);
        }

        var canReply = !t.IsDeleted && (v.IsSupport ? v.CanReply : TicketRules.RequesterCanReply(t.Status));
        var canAttach = !t.IsDeleted && (v.IsSupport ? v.CanReply || v.CanNote : TicketRules.RequesterCanReply(t.Status));
        var allowed = v.CanChangeStatus
            ? TicketRules.StaffTargets(t.Status, v.CanReopenClosed).Select(s => s.ToString()).ToList()
            : [];

        return new TicketDetailDto(
            t.TicketId, t.TrackingId, t.Subject, t.Description, t.Status.ToString(), t.PriorityId, t.PriorityName,
            t.ServiceDeskId, t.ServiceDeskName, t.CategoryId, t.CategoryName, t.TicketTypeName, t.TeamId, t.TeamName,
            t.RequesterUserId, t.RequesterName, v.IsSupport || v.IsRequester ? t.RequesterEmail : string.Empty,
            t.AssigneeUserId, t.AssigneeName, ((TicketChannel)t.ChannelValue).ToString(),
            t.CreatedAt, t.UpdatedAt, t.FirstRespondedAt, t.ResolvedAt, v.IsSupport,
            t.IsDeleted, t.DeletedAt, v.IsSupport ? t.DeleteReason : null,
            new TicketActions(canReply, v.CanNote, v.CanChangeStatus, v.CanChangePriority, v.CanAssign,
                v.CanTransfer, v.CanDelete, v.CanRestore, canAttach, allowed),
            comments, attachments, history, sla, escalation);
    }

    /// <summary>Checks every file against the policy and writes them to storage. On any problem nothing is kept.</summary>
    private async Task<(List<StoredFile> Stored, OperationResult<TicketDetailDto>? Error)> StoreFilesAsync(
        IReadOnlyList<IncomingFile> files, CancellationToken ct)
    {
        var stored = new List<StoredFile>();
        if (files.Count == 0) return (stored, null);

        var limits = storageOptions.Value;
        if (files.Count > limits.MaxFilesPerUpload)
            return (stored, Fail(ErrorKind.Validation, "too_many_files", $"Attach up to {limits.MaxFilesPerUpload} files at a time."));

        // Check all files before storing any.
        var checkedFiles = new List<(IncomingFile File, string ContentType)>();
        foreach (var file in files)
        {
            var head = new byte[8];
            int read;
            await using (var probe = file.OpenReadStream())
            {
                read = await probe.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
            }
            var (contentType, error) = AttachmentPolicy.Check(file.FileName, file.Length, head.AsSpan(0, read), limits);
            if (error is not null) return (stored, Fail(ErrorKind.Validation, "invalid_file", error));
            checkedFiles.Add((file, contentType!));
        }

        try
        {
            foreach (var (file, contentType) in checkedFiles)
            {
                await using var content = file.OpenReadStream();
                var key = await storage.SaveAsync(content, Path.GetExtension(file.FileName), ct);
                stored.Add(new StoredFile(AttachmentPolicy.SafeName(file.FileName), contentType, file.Length, key));
            }
        }
        catch
        {
            await DeleteStoredAsync(stored);
            throw;
        }
        return (stored, null);
    }

    private async Task DeleteStoredAsync(IEnumerable<StoredFile> stored)
    {
        foreach (var file in stored)
        {
            try { await storage.DeleteAsync(file.StorageKey); }
            catch { /* best effort: an orphaned file is harmless and can be swept later */ }
        }
    }

    /// <summary>
    /// Runs after the change is committed, so it must not be cancelled when the browser disconnects:
    /// the change already happened and people still need to hear about it.
    /// TODO(Phase 3): write notifications in the same transaction as the change (outbox) so even a crash here can't lose them.
    /// </summary>
    private async Task NotifyAsync(TicketEvent evt, long ticketId, long actorId, string? message, CancellationToken _)
    {
        var ticket = await queries.GetAsync(ticketId, includeDeleted: false, CancellationToken.None);
        if (ticket is not null) await notifier.NotifyAsync(evt, ticket, actorId, message, CancellationToken.None);
    }

    private static string FileList(IEnumerable<StoredFile> files)
    {
        var names = string.Join(", ", files.Select(f => f.FileName));
        return names.Length > 200 ? names[..197] + "..." : names;
    }

    private static void ApplyStatus(TicketChange change, TicketStatus from, TicketStatus to)
    {
        if (from == to) return;
        change.Status = to;
        change.History.Add(new HistoryEntry(TicketActivities.StatusChanged, from.ToString(), to.ToString()));
        if (to == TicketStatus.Resolved) change.MarkResolved = true;
        if (to == TicketStatus.Closed) change.MarkClosed = true;
        if (from is TicketStatus.Resolved or TicketStatus.Closed && to is not (TicketStatus.Resolved or TicketStatus.Closed))
            change.ClearResolved = true;
    }

    private static string Describe(TicketStatus status) => status switch
    {
        TicketStatus.InProgress => "in-progress",
        TicketStatus.AwaitingRequester => "awaiting-requester",
        TicketStatus.PendingApproval => "pending-approval",
        _ => status.ToString().ToLowerInvariant()
    };

    private long RequireActor() => currentUser.UserId ?? throw new InvalidOperationException("Tickets require a signed-in user.");
    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private static OperationResult<TicketDetailDto> NotFound() =>
        OperationResult<TicketDetailDto>.Fail(ErrorKind.NotFound, "not_found", "This ticket does not exist or you do not have access to it.");

    private static OperationResult<TicketDetailDto> Fail(ErrorKind kind, string code, string message) =>
        OperationResult<TicketDetailDto>.Fail(kind, code, message);
}
