using System.Security.Cryptography;
using System.Text;
using TicketCore.Core.Auth;
using TicketCore.Models.Users;

namespace TicketCore.Core.Tests;

public class PasswordServiceTests
{
    private readonly PasswordService _service = new();

    [Fact]
    public void New_hash_verifies()
    {
        var user = new User { UserName = "a" };
        user.PasswordHash = _service.Hash(user, "Correct-horse-42");

        Assert.Equal(PasswordCheck.Success, _service.Verify(user, "Correct-horse-42"));
        Assert.Equal(PasswordCheck.Failed, _service.Verify(user, "wrong-password-42"));
    }

    [Fact]
    public void Legacy_sha256_hash_is_accepted_and_flagged_for_rehash()
    {
        // Old VueTicket stored lowercase hex SHA-256 of the password, computed in the browser.
        var legacy = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("OldPassword1")));
        var user = new User { UserName = "old", LegacyPasswordHash = legacy };

        Assert.Equal(PasswordCheck.SuccessRehashNeeded, _service.Verify(user, "OldPassword1"));
        Assert.Equal(PasswordCheck.Failed, _service.Verify(user, "OldPassword2"));
    }

    [Fact]
    public void Legacy_hash_comparison_ignores_case()
    {
        var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("OldPassword1")));
        var user = new User { UserName = "old", LegacyPasswordHash = legacy };

        Assert.Equal(PasswordCheck.SuccessRehashNeeded, _service.Verify(user, "OldPassword1"));
    }

    [Fact]
    public void User_without_any_hash_never_verifies()
    {
        Assert.Equal(PasswordCheck.Failed, _service.Verify(new User { UserName = "x" }, ""));
    }

    [Theory]
    [InlineData("short1", false)]
    [InlineData("onlyletterslong", false)]
    [InlineData("1234567890", false)]
    [InlineData("letters-and-1", true)]
    public void Validate_enforces_length_letters_and_digits(string password, bool ok)
    {
        Assert.Equal(ok, _service.Validate(password) is null);
    }
}
