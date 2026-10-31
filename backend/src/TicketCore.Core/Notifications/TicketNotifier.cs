using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketCore.Common;
using TicketCore.Data.Messaging;
using TicketCore.Data.Tickets.Queries;
using TicketCore.Data.Users.Queries;

namespace TicketCore.Core.Notifications;

public enum TicketEvent
{
    Created,
    SupportReplied,
    RequesterReplied,
    Assigned,
    Resolved
}

/// <summary>
/// Turns ticket events into in-app notifications and queued emails. Nobody is notified about their own action.
/// Delivery is best-effort: a failure here is logged and never undoes the ticket change.
/// </summary>
public interface ITicketNotifier
{
    /// <param name="ticket">The ticket after the change.</param>
    /// <param name="actorUserId">Who did it; null when the automation engine did it.</param>
    /// <param name="message">The public reply text, for reply events.</param>
    Task NotifyAsync(TicketEvent evt, TicketRecord ticket, long? actorUserId, string? message, CancellationToken ct = default);

    /// <summary>Notifies specific staff (in-app and email), for engine events such as SLA reminders and escalations.</summary>
    Task NotifyStaffAsync(IEnumerable<long> userIds, TicketRecord ticket, string type, string title, string intro, CancellationToken ct = default);
}

public sealed class TicketNotifier(
    INotificationStore notifications,
    IEmailQueue emails,
    IUserQueries users,
    IOptions<AppOptions> app,
    TimeProvider clock,
    ILogger<TicketNotifier> logger) : ITicketNotifier
{
    public async Task NotifyStaffAsync(IEnumerable<long> userIds, TicketRecord ticket, string type, string title, string intro, CancellationToken ct = default)
    {
        try
        {
            var link = $"{app.Value.PublicUrl.TrimEnd('/')}/tickets/{ticket.TicketId}";
            var inApp = new List<NewNotification>();
            var outbox = new List<NewEmail>();
            foreach (var userId in userIds.Distinct())
            {
                var user = await users.GetByIdAsync(userId, ct);
                if (user is null || !user.IsActive) continue;
                inApp.Add(new NewNotification(userId, ticket.TicketId, type, title));
                outbox.Add(Email(user.Email, user.DisplayName, title, intro, null, link, ticket));
            }
            var now = clock.GetUtcNow().UtcDateTime;
            await notifications.AddAsync(inApp, now, ct);
            await emails.EnqueueAsync(outbox, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not queue {Type} notifications on ticket {TicketId}", type, ticket.TicketId);
        }
    }

    public async Task NotifyAsync(TicketEvent evt, TicketRecord ticket, long? actorUserId, string? message, CancellationToken ct = default)
    {
        try
        {
            var actor = actorUserId is { } id ? await users.GetByIdAsync(id, ct) : null;
            var actorName = actor?.DisplayName ?? (actorUserId is null ? app.Value.ProductName : "Someone");
            var link = $"{app.Value.PublicUrl.TrimEnd('/')}/tickets/{ticket.TicketId}";
            var inApp = new List<NewNotification>();
            var outbox = new List<NewEmail>();

            void ToRequester(string title, string intro, string? body)
            {
                if (ticket.RequesterUserId == actorUserId) return;
                inApp.Add(new NewNotification(ticket.RequesterUserId, ticket.TicketId, evt.ToString(), title));
                outbox.Add(Email(ticket.RequesterEmail, ticket.RequesterName, title, intro, body, link, ticket));
            }

            void ToAssignee(string title, string intro, string? body)
            {
                if (ticket.AssigneeUserId is not { } assigneeId || assigneeId == actorUserId) return;
                inApp.Add(new NewNotification(assigneeId, ticket.TicketId, evt.ToString(), title));
                if (!string.IsNullOrEmpty(ticket.AssigneeEmail))
                    outbox.Add(Email(ticket.AssigneeEmail, ticket.AssigneeName, title, intro, body, link, ticket));
            }

            switch (evt)
            {
                case TicketEvent.Created:
                    // Confirmation email only; the requester just did this (or an agent did it for them).
                    outbox.Add(Email(ticket.RequesterEmail, ticket.RequesterName,
                        $"[{ticket.TrackingId}] We received your request: {ticket.Subject}",
                        ticket.RequesterUserId == actorUserId
                            ? $"Thanks, your request {ticket.TrackingId} has been received by {ticket.ServiceDeskName}. We'll reply here as soon as possible."
                            : $"{actorName} raised request {ticket.TrackingId} with {ticket.ServiceDeskName} for you.",
                        ticket.Description, link, ticket));
                    break;
                case TicketEvent.SupportReplied:
                    ToRequester($"[{ticket.TrackingId}] {actorName} replied to your request",
                        $"{actorName} replied to your request \"{ticket.Subject}\".", message);
                    break;
                case TicketEvent.RequesterReplied:
                    ToAssignee($"[{ticket.TrackingId}] {ticket.RequesterName} replied",
                        $"{ticket.RequesterName} replied on \"{ticket.Subject}\".", message);
                    break;
                case TicketEvent.Assigned:
                    ToAssignee($"[{ticket.TrackingId}] Assigned to you: {ticket.Subject}",
                        $"{actorName} assigned {ticket.TrackingId} ({ticket.PriorityName} priority) to you.", ticket.Description);
                    break;
                case TicketEvent.Resolved:
                    ToRequester($"[{ticket.TrackingId}] Your request has been resolved",
                        $"{actorName} marked \"{ticket.Subject}\" as resolved. If it isn't fixed, reply and we'll pick it up again.", null);
                    break;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            await notifications.AddAsync(inApp, now, ct);
            await emails.EnqueueAsync(outbox.Where(e => !string.IsNullOrWhiteSpace(e.ToAddress)).ToList(), now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not queue notifications for {Event} on ticket {TicketId}", evt, ticket.TicketId);
        }
    }

    private NewEmail Email(string to, string? toName, string subject, string intro, string? body, string link, TicketRecord ticket)
    {
        var product = app.Value.ProductName;

        var text = new StringBuilder()
            .AppendLine(intro).AppendLine();
        if (!string.IsNullOrWhiteSpace(body)) text.AppendLine(body).AppendLine();
        text.AppendLine($"Open the request: {link}")
            .AppendLine().AppendLine($"{ticket.TrackingId} · {ticket.ServiceDeskName} · {ticket.CategoryName}")
            .AppendLine($"This message was sent by {product}.");

        static string Enc(string s) => WebUtility.HtmlEncode(s);
        var html = new StringBuilder()
            .Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#16202C;max-width:640px\">")
            .Append($"<p>{Enc(intro)}</p>");
        if (!string.IsNullOrWhiteSpace(body))
            html.Append($"<div style=\"border-left:3px solid #1F4FB4;padding:8px 12px;margin:12px 0;white-space:pre-wrap\">{Enc(body)}</div>");
        html.Append($"<p><a href=\"{Enc(link)}\" style=\"color:#1F4FB4\">Open {Enc(ticket.TrackingId)}</a></p>")
            .Append($"<p style=\"color:#566273;font-size:12px\">{Enc(ticket.TrackingId)} · {Enc(ticket.ServiceDeskName)} · {Enc(ticket.CategoryName)}<br>This message was sent by {Enc(product)}.</p>")
            .Append("</div>");

        return new NewEmail(to, toName, subject, text.ToString(), html.ToString(), ticket.TicketId);
    }
}
