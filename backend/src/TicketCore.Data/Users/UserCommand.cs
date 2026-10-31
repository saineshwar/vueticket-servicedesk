using Microsoft.EntityFrameworkCore;
using TicketCore.Models.Users;

namespace TicketCore.Data.Users.Commands;

public interface IUserCommand
{
    Task<long> AddAsync(User user, CancellationToken ct = default);

    /// <summary>Saves changes to tracked users.</summary>
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>
    /// Records a successful login in one atomic update (safe when the same person signs in from two devices at once).
    /// Pass a new hash to upgrade a legacy password at the same time.
    /// </summary>
    Task RecordLoginSuccessAsync(long userId, DateTime now, string? upgradedPasswordHash, CancellationToken ct = default);

    /// <summary>
    /// Counts a failed login atomically and locks the account when the limit is reached.
    /// Returns true when this attempt caused a lockout. Concurrent attempts cannot skip the counter.
    /// </summary>
    Task<bool> RecordLoginFailureAsync(long userId, int maxFailedLogins, DateTime lockoutUntil, CancellationToken ct = default);
}

public sealed class UserCommand(TicketDbContext db) : IUserCommand
{
    public async Task<long> AddAsync(User user, CancellationToken ct = default)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user.UserId;
    }

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task RecordLoginSuccessAsync(long userId, DateTime now, string? upgradedPasswordHash, CancellationToken ct = default)
    {
        if (upgradedPasswordHash is null)
        {
            await db.Users.Where(u => u.UserId == userId).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FailedLoginCount, 0)
                .SetProperty(u => u.LockoutEndAt, (DateTime?)null)
                .SetProperty(u => u.LastLoginAt, now), ct);
        }
        else
        {
            await db.Users.Where(u => u.UserId == userId).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FailedLoginCount, 0)
                .SetProperty(u => u.LockoutEndAt, (DateTime?)null)
                .SetProperty(u => u.LastLoginAt, now)
                .SetProperty(u => u.PasswordHash, upgradedPasswordHash)
                .SetProperty(u => u.LegacyPasswordHash, (string?)null), ct);
        }
    }

    public async Task<bool> RecordLoginFailureAsync(long userId, int maxFailedLogins, DateTime lockoutUntil, CancellationToken ct = default)
    {
        // One statement: increment, and if the limit is reached, lock and reset the counter.
        var lockedNow = await db.Database.SqlQuery<bool>($"""
            UPDATE dbo.[User]
            SET FailedLoginCount = CASE WHEN FailedLoginCount + 1 >= {maxFailedLogins} THEN 0 ELSE FailedLoginCount + 1 END,
                LockoutEndAt     = CASE WHEN FailedLoginCount + 1 >= {maxFailedLogins} THEN {lockoutUntil} ELSE LockoutEndAt END
            OUTPUT CAST(CASE WHEN deleted.FailedLoginCount + 1 >= {maxFailedLogins} THEN 1 ELSE 0 END AS BIT) AS [Value]
            WHERE UserId = {userId}
            """).ToListAsync(ct);
        return lockedNow.FirstOrDefault();
    }
}
