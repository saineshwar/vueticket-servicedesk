using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using TicketCore.Common;
using TicketCore.Data.Messaging;

namespace TicketCore.Services.Email;

/// <summary>Delivers one message. Pickup writes .eml files; SMTP sends through the configured server.</summary>
public interface IEmailTransport
{
    Task SendAsync(MimeMessage message, CancellationToken ct);
}

public sealed class PickupDirectoryTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    public async Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        var folder = Path.GetFullPath(options.Value.PickupPath);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml");
        await message.WriteToAsync(path, ct);
    }
}

public sealed class SmtpTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    public async Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        var o = options.Value;
        using var client = new SmtpClient();
        await client.ConnectAsync(o.SmtpHost, o.SmtpPort, o.SmtpStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrEmpty(o.SmtpUserName)) await client.AuthenticateAsync(o.SmtpUserName, o.SmtpPassword ?? string.Empty, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>
/// Sends queued email in the background with retries (1, 5, 15, 60 and 240 minutes, then gives up).
/// Moves to the Hangfire worker host in Phase 3; claiming rows with a lease already makes it safe to run on several nodes.
/// </summary>
public sealed class EmailDispatcher(
    IServiceScopeFactory scopes,
    IEmailTransport transport,
    IOptions<EmailOptions> options,
    TimeProvider clock,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(4)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try
            {
                await SendDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Email dispatch cycle failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SendDueAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IEmailQueue>();
        var now = clock.GetUtcNow().UtcDateTime;
        var batch = await queue.ClaimDueAsync(20, now, TimeSpan.FromMinutes(5), ct);

        foreach (var email in batch)
        {
            try
            {
                await transport.SendAsync(ToMime(email), ct);
                await queue.MarkSentAsync(email.EmailId, clock.GetUtcNow().UtcDateTime, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var giveUp = email.Attempts + 1 > Backoff.Length;
                var next = clock.GetUtcNow().UtcDateTime + Backoff[Math.Min(email.Attempts, Backoff.Length - 1)];
                logger.LogWarning(ex, "Email {EmailId} to {To} failed (attempt {Attempt})", email.EmailId, email.ToAddress, email.Attempts + 1);
                await queue.MarkFailedAttemptAsync(email.EmailId, ex.Message, next, giveUp, clock.GetUtcNow().UtcDateTime, ct);
            }
        }
    }

    private MimeMessage ToMime(QueuedEmail email)
    {
        var o = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        message.To.Add(new MailboxAddress(email.ToName ?? email.ToAddress, email.ToAddress));
        message.Subject = email.Subject;
        message.Body = new BodyBuilder { TextBody = email.BodyText, HtmlBody = email.BodyHtml }.ToMessageBody();
        return message;
    }
}
