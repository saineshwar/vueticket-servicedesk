namespace TicketCore.ViewModels.Tickets;

/// <summary>TicketCommentId links a file to the reply or note it was sent with; null means attached to the ticket itself.</summary>
public sealed record AttachmentDto(
    long AttachmentId,
    long? TicketCommentId,
    string FileName,
    string ContentType,
    long SizeBytes,
    bool IsInternal,
    string UploadedByName,
    DateTime UploadedAt);

public sealed record TransferTicketRequest(int TeamId);

public sealed record DeleteTicketRequest(string Reason);

/// <summary>A file received by the API, before it is stored.</summary>
public sealed record IncomingFile(string FileName, string ContentType, long Length, Func<Stream> OpenReadStream);
