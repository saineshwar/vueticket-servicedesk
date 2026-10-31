using TicketCore.Common;

namespace TicketCore.Models.Users;

public class User
{
    public long UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Mobile { get; set; }
    public UserType UserType { get; set; }
    public int? HomeOrgUnitId { get; set; }
    public long? ManagerUserId { get; set; }

    /// <summary>ASP.NET Core Identity PBKDF2 hash.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Unsalted SHA-256 hex from the old VueTicket. Cleared after the first successful login.</summary>
    public string? LegacyPasswordHash { get; set; }

    public bool IsActive { get; set; } = true;
    public bool EmailVerified { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndAt { get; set; }
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
    public string? ExternalId { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public string DisplayName =>
        string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName)
            ? UserName
            : $"{FirstName} {LastName}".Trim();
}
