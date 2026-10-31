using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using TicketCore.Models.Users;

namespace TicketCore.Core.Auth;

public enum PasswordCheck
{
    Failed,
    Success,

    /// <summary>Correct, but stored in an old format; the caller should save a new hash.</summary>
    SuccessRehashNeeded
}

public interface IPasswordService
{
    string Hash(User user, string password);
    PasswordCheck Verify(User user, string password);

    /// <summary>Returns a reason the password is not acceptable, or null when it is.</summary>
    string? Validate(string password);
}

public sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public PasswordCheck Verify(User user, string password)
    {
        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            return _hasher.VerifyHashedPassword(user, user.PasswordHash, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed
            };
        }

        // Old VueTicket stored an unsalted SHA-256 of the password, as lowercase hex.
        if (!string.IsNullOrEmpty(user.LegacyPasswordHash))
        {
            var candidate = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
            var stored = user.LegacyPasswordHash.Trim().ToLowerInvariant();
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), Encoding.ASCII.GetBytes(stored)))
            {
                return PasswordCheck.SuccessRehashNeeded;
            }
        }

        return PasswordCheck.Failed;
    }

    public string? Validate(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10)
            return "Use at least 10 characters.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Use at least one letter and one number.";
        return null;
    }
}
