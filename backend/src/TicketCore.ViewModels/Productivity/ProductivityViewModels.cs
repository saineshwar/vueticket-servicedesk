namespace TicketCore.ViewModels.Productivity;

/// <summary>A saved reply. Personal ones belong to one agent; team ones are shared with everyone who works that team.</summary>
public sealed record CannedResponseDto(
    int CannedResponseId,
    string Title,
    string Body,
    int? TeamId,
    string? TeamName,
    bool IsPersonal,
    bool CanEdit);

/// <summary>Null TeamId makes a personal saved reply.</summary>
public sealed record SaveCannedResponseRequest(string Title, string Body, int? TeamId);

public sealed record NotificationDto(long NotificationId, long? TicketId, string Type, string Title, DateTime CreatedAt, DateTime? ReadAt);

public sealed record NotificationsResponse(IReadOnlyList<NotificationDto> Items, int UnreadCount);
