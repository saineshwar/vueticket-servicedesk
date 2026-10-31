using Microsoft.EntityFrameworkCore;
using TicketCore.Models.Access;

namespace TicketCore.Data.Auth;

public interface IRefreshTokenQueries
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
}

public interface IRefreshTokenCommand
{
    Task AddAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>Marks a token as used and records which token replaced it.</summary>
    Task RotateAsync(long refreshTokenId, string replacedByHash, DateTime now, CancellationToken ct = default);

    /// <summary>Revokes every live token in a family (used when a stolen token is replayed).</summary>
    Task RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken ct = default);

    Task RevokeAsync(string tokenHash, DateTime now, CancellationToken ct = default);
}

public sealed class RefreshTokenStore(TicketDbContext db) : IRefreshTokenQueries, IRefreshTokenCommand
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
    }

    public Task RotateAsync(long refreshTokenId, string replacedByHash, DateTime now, CancellationToken ct = default) =>
        db.RefreshTokens.Where(t => t.RefreshTokenId == refreshTokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.ReplacedByTokenHash, replacedByHash), ct);

    public Task RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken ct = default) =>
        db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

    public Task RevokeAsync(string tokenHash, DateTime now, CancellationToken ct = default) =>
        db.RefreshTokens.Where(t => t.TokenHash == tokenHash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
}
