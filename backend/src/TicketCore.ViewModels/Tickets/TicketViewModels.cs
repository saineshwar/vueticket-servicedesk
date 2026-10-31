namespace TicketCore.ViewModels.Tickets;

/// <summary>RequesterUserId is set only when support staff raise a ticket for someone else.</summary>
public sealed record CreateTicketRequest(
    int ServiceDeskId,
    int CategoryId,
    string Subject,
    string Description,
    int? PriorityId,
    long? RequesterUserId);

public sealed record TicketListItemDto(
    long TicketId,
    string TrackingId,
    string Subject,
    string Status,
    int PriorityId,
    string PriorityName,
    string ServiceDeskName,
    string CategoryName,
    string TeamName,
    string RequesterName,
    string? AssigneeName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? SlaDueAt,
    bool SlaPaused,
    int? EscalationLevel);

/// <summary>Which ticket list to show. Every view is limited to what the caller may see.</summary>
public enum TicketView
{
    /// <summary>Tickets the caller raised (or was raised for).</summary>
    Requested,
    /// <summary>Tickets assigned to the caller.</summary>
    Mine,
    /// <summary>All tickets in teams where the caller has ticket.view.</summary>
    Team,
    /// <summary>Unassigned tickets in teams where the caller has queue.unassigned.</summary>
    Unassigned,
    /// <summary>Deleted tickets in teams where the caller has ticket.restore.</summary>
    Deleted,
    /// <summary>Escalated tickets in teams where the caller has queue.escalated, plus escalations the caller owns.</summary>
    Escalated
}

public sealed record TicketListQuery(TicketView View, string? Status, string? Q, int Page = 1, int PageSize = 25);

public sealed record TicketCommentDto(
    long TicketCommentId,
    long AuthorUserId,
    string AuthorName,
    bool AuthorIsSupport,
    bool IsInternal,
    string Body,
    DateTime CreatedAt);

public sealed record TicketHistoryDto(string Activity, string? FromValue, string? ToValue, string? UserName, DateTime OccurredAt);

/// <summary>What the caller may do on this ticket; the UI shows controls only for these.</summary>
public sealed record TicketActions(
    bool CanReply,
    bool CanAddNote,
    bool CanChangeStatus,
    bool CanChangePriority,
    bool CanAssign,
    bool CanTransfer,
    bool CanDelete,
    bool CanRestore,
    bool CanAttach,
    IReadOnlyList<string> AllowedStatuses);

public sealed record TicketDetailDto(
    long TicketId,
    string TrackingId,
    string Subject,
    string Description,
    string Status,
    int PriorityId,
    string PriorityName,
    int ServiceDeskId,
    string ServiceDeskName,
    int CategoryId,
    string CategoryName,
    string TicketTypeName,
    int TeamId,
    string TeamName,
    long RequesterUserId,
    string RequesterName,
    string RequesterEmail,
    long? AssigneeUserId,
    string? AssigneeName,
    string Channel,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? FirstRespondedAt,
    DateTime? ResolvedAt,
    bool ViewerIsSupport,
    bool IsDeleted,
    DateTime? DeletedAt,
    string? DeleteReason,
    TicketActions Actions,
    IReadOnlyList<TicketCommentDto> Comments,
    IReadOnlyList<AttachmentDto> Attachments,
    IReadOnlyList<TicketHistoryDto> History,
    TicketCore.ViewModels.Sla.TicketSlaDto? Sla,
    TicketCore.ViewModels.Sla.TicketEscalationDto? Escalation);

/// <summary>NewStatus is optional; support staff may change status in the same step as replying.</summary>
public sealed record AddCommentRequest(string Body, bool IsInternal, string? NewStatus);

public sealed record ChangeStatusRequest(string Status);

public sealed record ChangePriorityRequest(int PriorityId);

/// <summary>Null AssigneeUserId unassigns.</summary>
public sealed record AssignTicketRequest(long? AssigneeUserId);

public sealed record AssignableUserDto(long UserId, string DisplayName);
