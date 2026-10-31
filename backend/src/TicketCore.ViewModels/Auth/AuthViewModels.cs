namespace TicketCore.ViewModels.Auth;

public sealed record LoginRequest(string UserNameOrEmail, string Password);

/// <summary>Returned on login and refresh. The refresh token travels only in an httpOnly cookie.</summary>
public sealed record AccessTokenResponse(string AccessToken, DateTime ExpiresAt);

/// <summary>Internal result of a login or refresh, before the API splits it into body and cookie.</summary>
public sealed record IssuedTokens(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt);

public sealed record SetupStatusResponse(bool IsInitialized);

public sealed record SetupRequest(
    string OrganizationName,
    string OrganizationCode,
    string AdminUserName,
    string AdminEmail,
    string AdminFirstName,
    string AdminLastName,
    string AdminPassword);
