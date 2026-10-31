using Microsoft.Extensions.Options;
using TicketCore.Common;
using TicketCore.Data.Audit;
using TicketCore.Data.Auth;
using TicketCore.Data.Users.Commands;
using TicketCore.Data.Users.Queries;
using TicketCore.Models.Access;
using TicketCore.Models.Users;
using TicketCore.ViewModels.Auth;

namespace TicketCore.Core.Auth;

public interface IAuthService
{
    Task<OperationResult<IssuedTokens>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<OperationResult<IssuedTokens>> RefreshAsync(string rawRefreshToken, CancellationToken ct = default);
    Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default);
}

public sealed class AuthService(
    IUserQueries userQueries,
    IUserCommand userCommand,
    IRefreshTokenQueries refreshTokenQueries,
    IRefreshTokenCommand refreshTokenCommand,
    IPasswordService passwords,
    ITokenService tokens,
    IAuditCommand audit,
    ICurrentUser currentUser,
    IOptions<JwtOptions> jwtOptions,
    IOptions<SecurityOptions> securityOptions,
    TimeProvider clock) : IAuthService
{
    private const string InvalidCredentials = "The username or password is incorrect.";

    public async Task<OperationResult<IssuedTokens>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var user = await userQueries.GetByUserNameOrEmailAsync(request.UserNameOrEmail.Trim(), ct);

        if (user is null || !user.IsActive)
        {
            await audit.WriteAsync(user?.UserId, "auth.login.failed", "User", user?.UserId.ToString(),
                new { reason = user is null ? "unknown user" : "inactive" }, currentUser.IpAddress, ct);
            return Fail(ErrorKind.Unauthorized, "invalid_credentials", InvalidCredentials);
        }

        if (user.LockoutEndAt is { } lockoutEnd && lockoutEnd > now)
        {
            return Fail(ErrorKind.Unauthorized, "locked_out",
                "This account is locked after too many failed attempts. Try again in a few minutes.");
        }

        var check = passwords.Verify(user, request.Password);
        if (check == PasswordCheck.Failed)
        {
            await RecordFailedAttemptAsync(user, now, ct);
            return Fail(ErrorKind.Unauthorized, "invalid_credentials", InvalidCredentials);
        }

        if (user.UserType == UserType.Customer && !user.EmailVerified)
        {
            return Fail(ErrorKind.Unauthorized, "email_not_verified",
                "Verify your email address first. Use the link we sent when you registered.");
        }

        var upgradedHash = check == PasswordCheck.SuccessRehashNeeded ? passwords.Hash(user, request.Password) : null;
        await userCommand.RecordLoginSuccessAsync(user.UserId, now, upgradedHash, ct);

        var issued = await IssueAsync(user, Guid.NewGuid(), now, ct);
        await audit.WriteAsync(user.UserId, "auth.login", "User", user.UserId.ToString(), null, currentUser.IpAddress, ct);
        return OperationResult<IssuedTokens>.Ok(issued);
    }

    public async Task<OperationResult<IssuedTokens>> RefreshAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var stored = await refreshTokenQueries.GetByHashAsync(tokens.HashRefreshToken(rawRefreshToken), ct);

        if (stored is null || stored.ExpiresAt <= now)
            return Fail(ErrorKind.Unauthorized, "session_expired", "Your session has expired. Sign in again.");

        if (stored.RevokedAt is not null && stored.ReplacedByTokenHash is null)
        {
            // Ended by logout or an earlier revocation: an ordinary expired session, not an attack.
            return Fail(ErrorKind.Unauthorized, "session_expired", "Your session has expired. Sign in again.");
        }

        if (stored.RevokedAt is not null)
        {
            // An already-rotated token came back: someone may have copied it. End the whole session family.
            await refreshTokenCommand.RevokeFamilyAsync(stored.FamilyId, now, ct);
            await audit.WriteAsync(stored.UserId, "auth.refresh.reuse_detected", "User", stored.UserId.ToString(),
                new { stored.FamilyId }, currentUser.IpAddress, ct);
            return Fail(ErrorKind.Unauthorized, "session_expired", "Your session has expired. Sign in again.");
        }

        var user = await userQueries.GetByIdAsync(stored.UserId, ct);
        if (user is null || !user.IsActive)
        {
            await refreshTokenCommand.RevokeFamilyAsync(stored.FamilyId, now, ct);
            return Fail(ErrorKind.Unauthorized, "session_expired", "Your session has expired. Sign in again.");
        }

        var issued = await IssueAsync(user, stored.FamilyId, now, ct);
        await refreshTokenCommand.RotateAsync(stored.RefreshTokenId, tokens.HashRefreshToken(issued.RefreshToken), now, ct);
        return OperationResult<IssuedTokens>.Ok(issued);
    }

    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rawRefreshToken)) return;
        var now = clock.GetUtcNow().UtcDateTime;
        var stored = await refreshTokenQueries.GetByHashAsync(tokens.HashRefreshToken(rawRefreshToken), ct);
        if (stored is null) return;

        await refreshTokenCommand.RevokeFamilyAsync(stored.FamilyId, now, ct);
        await audit.WriteAsync(stored.UserId, "auth.logout", "User", stored.UserId.ToString(), null, currentUser.IpAddress, ct);
    }

    private async Task<IssuedTokens> IssueAsync(User user, Guid familyId, DateTime now, CancellationToken ct)
    {
        var (accessToken, accessExpires) = tokens.CreateAccessToken(user);
        var refreshToken = tokens.CreateRefreshToken();
        var refreshExpires = now.AddDays(jwtOptions.Value.RefreshTokenDays);

        await refreshTokenCommand.AddAsync(new RefreshToken
        {
            UserId = user.UserId,
            TokenHash = tokens.HashRefreshToken(refreshToken),
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = refreshExpires,
            CreatedByIp = currentUser.IpAddress
        }, ct);

        return new IssuedTokens(accessToken, accessExpires, refreshToken, refreshExpires);
    }

    private async Task RecordFailedAttemptAsync(User user, DateTime now, CancellationToken ct)
    {
        var settings = securityOptions.Value;
        var lockedOut = await userCommand.RecordLoginFailureAsync(
            user.UserId, settings.MaxFailedLogins, now.AddMinutes(settings.LockoutMinutes), ct);
        await audit.WriteAsync(user.UserId, lockedOut ? "auth.lockout" : "auth.login.failed", "User", user.UserId.ToString(),
            null, currentUser.IpAddress, ct);
    }

    private static OperationResult<IssuedTokens> Fail(ErrorKind kind, string code, string message) =>
        OperationResult<IssuedTokens>.Fail(kind, code, message);
}
