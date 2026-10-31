using System.Net.Mail;
using TicketCore.Common;
using TicketCore.Core.Access;
using TicketCore.Core.Auth;
using TicketCore.Data.Audit;
using TicketCore.Data.Organization.Queries;
using TicketCore.Data.Users.Commands;
using TicketCore.Data.Users.Queries;
using TicketCore.Models.Users;
using TicketCore.ViewModels.Users;

namespace TicketCore.Core.Users;

public interface IUserService
{
    /// <summary>Creates a user on behalf of the signed-in user, who needs user.manage on the new user's home unit.</summary>
    Task<OperationResult<UserListItemDto>> CreateAsync(CreateUserRequest request, CancellationToken ct = default);

    /// <summary>First-run only: creates the first administrator. Fails once any user exists.</summary>
    Task<OperationResult<UserListItemDto>> CreateInitialAdminAsync(CreateUserRequest request, CancellationToken ct = default);
}

public sealed class UserService(
    IUserQueries queries,
    IUserCommand command,
    IOrgUnitQueries orgUnitQueries,
    IPasswordService passwords,
    IPermissionEvaluator permissions,
    IAuditCommand audit,
    ICurrentUser currentUser,
    TimeProvider clock) : IUserService
{
    public async Task<OperationResult<UserListItemDto>> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } actorId)
            return Fail(ErrorKind.Unauthorized, "unauthorized", "Sign in first.");

        var allowed = request.HomeOrgUnitId is { } homeUnit
            ? await permissions.CanAsync(actorId, Permissions.UserManage, homeUnit, ct)
            : (await permissions.GetAccessAsync(actorId, ct)).HasAnywhere(Permissions.UserManage);
        if (!allowed) return Fail(ErrorKind.Forbidden, "forbidden", "You cannot create users in this unit.");

        return await CreateCoreAsync(request, ct);
    }

    public async Task<OperationResult<UserListItemDto>> CreateInitialAdminAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        if (await queries.AnyAsync(ct))
            return Fail(ErrorKind.Conflict, "already_initialized", "VueTicket is already set up.");
        return await CreateCoreAsync(request, ct);
    }

    private async Task<OperationResult<UserListItemDto>> CreateCoreAsync(CreateUserRequest request, CancellationToken ct)
    {
        var userName = request.UserName?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;

        if (userName.Length is < 3 or > 100) return Fail(ErrorKind.Validation, "invalid_username", "Use a username of 3 to 100 characters.");
        if (!MailAddress.TryCreate(email, out _)) return Fail(ErrorKind.Validation, "invalid_email", "Enter a valid email address.");
        if (!Enum.TryParse<UserType>(request.UserType, true, out var type))
            return Fail(ErrorKind.Validation, "invalid_type", "Choose Customer, Employee or Staff.");

        var passwordProblem = passwords.Validate(request.Password);
        if (passwordProblem is not null) return Fail(ErrorKind.Validation, "weak_password", passwordProblem);

        if (request.HomeOrgUnitId is { } unitId && await orgUnitQueries.GetByIdAsync(unitId, ct) is null)
            return Fail(ErrorKind.Validation, "unit_not_found", "The home unit does not exist.");
        if (request.ManagerUserId is { } managerId && await queries.GetByIdAsync(managerId, ct) is null)
            return Fail(ErrorKind.Validation, "manager_not_found", "The manager does not exist.");

        if (await queries.UserNameExistsAsync(userName, ct)) return Fail(ErrorKind.Conflict, "username_taken", "This username is already taken.");
        if (await queries.EmailExistsAsync(email, ct)) return Fail(ErrorKind.Conflict, "email_taken", "This email address is already registered.");

        var user = new User
        {
            UserName = userName,
            Email = email,
            FirstName = request.FirstName?.Trim(),
            LastName = request.LastName?.Trim(),
            UserType = type,
            HomeOrgUnitId = request.HomeOrgUnitId,
            ManagerUserId = request.ManagerUserId,
            EmailVerified = type != UserType.Customer,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };
        user.PasswordHash = passwords.Hash(user, request.Password);

        var id = await command.AddAsync(user, ct);
        await audit.WriteAsync(currentUser.UserId, "user.created", "User", id.ToString(),
            new { userName, email, type = type.ToString() }, currentUser.IpAddress, ct);

        var created = (await queries.SearchAsync(userName, 1, 1, ct)).Items.First(u => u.UserId == id);
        return OperationResult<UserListItemDto>.Ok(created);
    }

    private static OperationResult<UserListItemDto> Fail(ErrorKind kind, string code, string message) =>
        OperationResult<UserListItemDto>.Fail(kind, code, message);
}
