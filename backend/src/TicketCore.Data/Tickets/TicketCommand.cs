using System.Data.Common;
using Dapper;
using TicketCore.Common;

namespace TicketCore.Data.Tickets.Commands;

public sealed record NewTicket(
    int ServiceDeskId,
    string DeskCode,
    int CategoryId,
    byte TicketTypeId,
    int TeamId,
    string Subject,
    string Description,
    byte PriorityId,
    long RequesterUserId,
    long CreatedByUserId,
    TicketChannel Channel,
    DateTime Now);

public sealed record HistoryEntry(string Activity, string? From, string? To);

public sealed record NewComment(string Body, bool IsInternal, bool AuthorIsSupport);

/// <summary>A file already written to storage, waiting to be recorded against the ticket.</summary>
public sealed record StoredFile(string FileName, string ContentType, long SizeBytes, string StorageKey);

/// <summary>
/// One change to a ticket, applied in a single transaction: optional comment, field updates and history rows.
/// Null fields are left as they are.
/// </summary>
public sealed class TicketChange(long ticketId, long actorUserId, DateTime now)
{
    public long TicketId { get; } = ticketId;
    public long ActorUserId { get; } = actorUserId;
    public DateTime Now { get; } = now;

    public TicketStatus? Status { get; set; }
    public byte? PriorityId { get; set; }
    public bool ChangeAssignee { get; set; }
    public long? AssigneeUserId { get; set; }
    public bool MarkFirstResponse { get; set; }
    public bool MarkResolved { get; set; }
    public bool ClearResolved { get; set; }
    public bool MarkClosed { get; set; }
    public int? TeamId { get; set; }

    /// <summary>Set to delete the ticket (soft delete) with this reason.</summary>
    public string? DeleteReason { get; set; }
    public bool Restore { get; set; }

    public NewComment? Comment { get; set; }

    /// <summary>Linked to <see cref="Comment"/> when there is one; internal if the comment is internal.</summary>
    public List<StoredFile> Attachments { get; } = [];
    public List<HistoryEntry> History { get; } = [];
}

public interface ITicketCommand
{
    Task<(long TicketId, string TrackingId)> CreateAsync(NewTicket ticket, CancellationToken ct = default);
    Task ApplyAsync(TicketChange change, CancellationToken ct = default);
}

public sealed class TicketCommand(ISqlConnectionFactory connections) : ITicketCommand
{
    public async Task<(long TicketId, string TrackingId)> CreateAsync(NewTicket t, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        var number = await db.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT NEXT VALUE FOR dbo.TicketNumber", transaction: tx, cancellationToken: ct));
        var trackingId = $"{t.DeskCode}-{number}";

        var ticketId = await db.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO dbo.Ticket (TrackingId, ServiceDeskId, CategoryId, TicketTypeId, TeamId, Subject, Description, Status,
                                    PriorityId, RequesterUserId, CreatedByUserId, Channel, CreatedAt, UpdatedAt)
            OUTPUT inserted.TicketId
            VALUES (@trackingId, @ServiceDeskId, @CategoryId, @TicketTypeId, @TeamId, @Subject, @Description, @status,
                    @PriorityId, @RequesterUserId, @CreatedByUserId, @channel, @Now, @Now)
            """, new
        {
            trackingId, t.ServiceDeskId, t.CategoryId, t.TicketTypeId, t.TeamId, t.Subject, t.Description,
            status = (byte)TicketStatus.New, t.PriorityId, t.RequesterUserId, t.CreatedByUserId, channel = (byte)t.Channel, t.Now
        }, tx, cancellationToken: ct));

        await InsertHistoryAsync(db, tx, ticketId, t.CreatedByUserId, t.Now, [new HistoryEntry(TicketActivities.Created, null, trackingId)], ct);
        await tx.CommitAsync(ct);
        return (ticketId, trackingId);
    }

    public async Task ApplyAsync(TicketChange c, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        long? commentId = null;
        if (c.Comment is { } comment)
        {
            commentId = await db.ExecuteScalarAsync<long>(new CommandDefinition("""
                INSERT INTO dbo.TicketComment (TicketId, AuthorUserId, Body, IsInternal, AuthorIsSupport, CreatedAt)
                OUTPUT inserted.TicketCommentId
                VALUES (@TicketId, @ActorUserId, @Body, @IsInternal, @AuthorIsSupport, @Now)
                """, new { c.TicketId, c.ActorUserId, comment.Body, comment.IsInternal, comment.AuthorIsSupport, c.Now }, tx, cancellationToken: ct));
        }

        foreach (var file in c.Attachments)
        {
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.Attachment (TicketId, TicketCommentId, IsInternal, FileName, ContentType, SizeBytes, StorageKey, UploadedByUserId, UploadedAt)
                VALUES (@TicketId, @commentId, @isInternal, @FileName, @ContentType, @SizeBytes, @StorageKey, @ActorUserId, @Now)
                """, new
            {
                c.TicketId, commentId, isInternal = c.Comment?.IsInternal ?? false,
                file.FileName, file.ContentType, file.SizeBytes, file.StorageKey, c.ActorUserId, c.Now
            }, tx, cancellationToken: ct));
        }

        var sets = new List<string> { "UpdatedAt = @Now" };
        if (c.TeamId is not null) sets.Add("TeamId = @TeamId");
        if (c.DeleteReason is not null) sets.Add("IsDeleted = 1, DeletedAt = @Now, DeletedByUserId = @ActorUserId, DeleteReason = @DeleteReason");
        if (c.Restore) sets.Add("IsDeleted = 0, DeletedAt = NULL, DeletedByUserId = NULL, DeleteReason = NULL");
        if (c.Status is not null) sets.Add("Status = @status");
        if (c.PriorityId is not null) sets.Add("PriorityId = @PriorityId");
        if (c.ChangeAssignee) sets.Add("AssigneeUserId = @AssigneeUserId");
        if (c.MarkFirstResponse) sets.Add("FirstRespondedAt = COALESCE(FirstRespondedAt, @Now)");
        if (c.MarkResolved) sets.Add("ResolvedAt = @Now");
        if (c.ClearResolved) sets.Add("ResolvedAt = NULL");
        if (c.MarkClosed) sets.Add("ClosedAt = @Now");

        await db.ExecuteAsync(new CommandDefinition(
            $"UPDATE dbo.Ticket SET {string.Join(", ", sets)} WHERE TicketId = @TicketId",
            new { c.TicketId, c.Now, status = (byte?)c.Status, c.PriorityId, c.AssigneeUserId, c.TeamId, c.ActorUserId, c.DeleteReason },
            tx, cancellationToken: ct));

        await InsertHistoryAsync(db, tx, c.TicketId, c.ActorUserId, c.Now, c.History, ct);
        await tx.CommitAsync(ct);
    }

    private static async Task InsertHistoryAsync(DbConnection db, DbTransaction tx, long ticketId, long? userId, DateTime now,
        IEnumerable<HistoryEntry> entries, CancellationToken ct)
    {
        foreach (var e in entries)
        {
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.TicketHistory (TicketId, UserId, Activity, FromValue, ToValue, OccurredAt)
                VALUES (@ticketId, @userId, @Activity, @From, @To, @now)
                """, new { ticketId, userId, e.Activity, e.From, e.To, now }, tx, cancellationToken: ct));
        }
    }
}
