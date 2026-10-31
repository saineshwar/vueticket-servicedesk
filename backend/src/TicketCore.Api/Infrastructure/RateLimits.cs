namespace TicketCore.Api.Infrastructure;

/// <summary>
/// Limits are per client IP. Large offices share one NAT address, so they are set for a whole office,
/// not one person; password guessing is stopped by the per-account lockout instead.
/// </summary>
public static class RateLimits
{
    /// <summary>Login and setup: 30 requests per minute per IP.</summary>
    public const string Auth = "auth";

    /// <summary>Token refresh: 600 per minute per IP. Refresh tokens are random and cannot be guessed.</summary>
    public const string Refresh = "refresh";
}
