namespace TicketCore.Core;

/// <summary>Who is making the current request. Implemented by the API from the HTTP context.</summary>
public interface ICurrentUser
{
    long? UserId { get; }
    string? IpAddress { get; }
}
