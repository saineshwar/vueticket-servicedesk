using System.Text.Json;
using TicketCore.Models.Access;

namespace TicketCore.Data.Audit;

public interface IAuditCommand
{
    Task WriteAsync(long? userId, string action, string? entityType, string? entityId, object? details, string? ipAddress, CancellationToken ct = default);
}

public sealed class AuditCommand(TicketDbContext db, TimeProvider clock) : IAuditCommand
{
    public async Task WriteAsync(long? userId, string action, string? entityType, string? entityId, object? details, string? ipAddress, CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAt = clock.GetUtcNow().UtcDateTime,
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details is null ? null : JsonSerializer.Serialize(details),
            IpAddress = ipAddress
        });
        await db.SaveChangesAsync(ct);
    }
}
