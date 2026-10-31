namespace TicketCore.Common;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "VueTicket";
    public string Audience { get; set; } = "VueTicket";

    /// <summary>At least 32 characters. Never commit it; set it with user-secrets or an environment variable.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}
