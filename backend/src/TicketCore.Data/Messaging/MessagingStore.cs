using Dapper;
using TicketCore.ViewModels.Productivity;

namespace TicketCore.Data.Messaging;

public sealed record NewNotification(long RecipientUserId, long? TicketId, string Type, string Title);

public sealed record NewEmail(string ToAddress, string? ToName, string Subject, string BodyText, string BodyHtml, long? TicketId);

public sealed class QueuedEmail
{
    public long EmailId { get; init; }
    public string ToAddress { get; init; } = string.Empty;
    public string? ToName { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string BodyText { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    public int Attempts { get; init; }
}

public interface INotificationStore
{
    Task AddAsync(IReadOnlyCollection<NewNotification> notifications, DateTime now, CancellationToken ct = default);
    Task<NotificationsResponse> ListAsync(long userId, int take, CancellationToken ct = default);
    Task MarkReadAsync(long userId, long? notificationId, DateTime now, CancellationToken ct = default);
}

public interface IEmailQueue
{
    Task EnqueueAsync(IReadOnlyCollection<NewEmail> emails, DateTime now, CancellationToken ct = default);

    /// <summary>Claims up to <paramref name="take"/> due emails by pushing their next attempt into the future, so two senders never send the same email.</summary>
    Task<IReadOnlyList<QueuedEmail>> ClaimDueAsync(int take, DateTime now, TimeSpan lease, CancellationToken ct = default);
    Task MarkSentAsync(long emailId, DateTime now, CancellationToken ct = default);
    Task MarkFailedAttemptAsync(long emailId, string error, DateTime nextAttemptAt, bool giveUp, DateTime now, CancellationToken ct = default);
}

public sealed class MessagingStore(ISqlConnectionFactory connections) : INotificationStore, IEmailQueue
{
    public async Task AddAsync(IReadOnlyCollection<NewNotification> notifications, DateTime now, CancellationToken ct = default)
    {
        if (notifications.Count == 0) return;
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.Notification (RecipientUserId, TicketId, Type, Title, CreatedAt)
            VALUES (@RecipientUserId, @TicketId, @Type, @Title, @now)
            """, notifications.Select(n => new { n.RecipientUserId, n.TicketId, n.Type, n.Title, now }), cancellationToken: ct));
    }

    public async Task<NotificationsResponse> ListAsync(long userId, int take, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var items = await db.QueryAsync<NotificationDto>(new CommandDefinition("""
            SELECT TOP (@take) NotificationId, TicketId, Type, Title, CreatedAt, ReadAt
            FROM dbo.Notification WHERE RecipientUserId = @userId
            ORDER BY CreatedAt DESC, NotificationId DESC
            """, new { userId, take }, cancellationToken: ct));
        var unread = await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM dbo.Notification WHERE RecipientUserId = @userId AND ReadAt IS NULL", new { userId }, cancellationToken: ct));
        return new NotificationsResponse(items.ToList(), unread);
    }

    public async Task MarkReadAsync(long userId, long? notificationId, DateTime now, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.Notification SET ReadAt = @now
            WHERE RecipientUserId = @userId AND ReadAt IS NULL AND (@notificationId IS NULL OR NotificationId = @notificationId)
            """, new { userId, notificationId, now }, cancellationToken: ct));
    }

    public async Task EnqueueAsync(IReadOnlyCollection<NewEmail> emails, DateTime now, CancellationToken ct = default)
    {
        if (emails.Count == 0) return;
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dbo.EmailQueue (ToAddress, ToName, Subject, BodyText, BodyHtml, TicketId, CreatedAt, NextAttemptAt)
            VALUES (@ToAddress, @ToName, @Subject, @BodyText, @BodyHtml, @TicketId, @now, @now)
            """, emails.Select(e => new { e.ToAddress, e.ToName, e.Subject, e.BodyText, e.BodyHtml, e.TicketId, now }), cancellationToken: ct));
    }

    public async Task<IReadOnlyList<QueuedEmail>> ClaimDueAsync(int take, DateTime now, TimeSpan lease, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        var rows = await db.QueryAsync<QueuedEmail>(new CommandDefinition("""
            WITH due AS (
                SELECT TOP (@take) * FROM dbo.EmailQueue WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE SentAt IS NULL AND FailedAt IS NULL AND NextAttemptAt <= @now
                ORDER BY NextAttemptAt
            )
            UPDATE due SET NextAttemptAt = @leaseUntil
            OUTPUT inserted.EmailId, inserted.ToAddress, inserted.ToName, inserted.Subject, inserted.BodyText, inserted.BodyHtml, inserted.Attempts
            """, new { take, now, leaseUntil = now + lease }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task MarkSentAsync(long emailId, DateTime now, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition(
            "UPDATE dbo.EmailQueue SET SentAt = @now, Attempts = Attempts + 1, LastError = NULL WHERE EmailId = @emailId",
            new { emailId, now }, cancellationToken: ct));
    }

    public async Task MarkFailedAttemptAsync(long emailId, string error, DateTime nextAttemptAt, bool giveUp, DateTime now, CancellationToken ct = default)
    {
        await using var db = await connections.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.EmailQueue
            SET Attempts = Attempts + 1, LastError = @error, NextAttemptAt = @nextAttemptAt,
                FailedAt = CASE WHEN @giveUp = 1 THEN @now ELSE NULL END
            WHERE EmailId = @emailId
            """, new { emailId, error = error.Length > 1000 ? error[..1000] : error, nextAttemptAt, giveUp, now }, cancellationToken: ct));
    }
}
