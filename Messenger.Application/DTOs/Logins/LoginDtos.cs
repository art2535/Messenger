namespace Messenger.Application.DTOs.Logins;

public sealed class CreateLoginDto
{
    public Guid LoginId { get; init; }
    public Guid UserId { get; init; }
    public string TokenHash { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public DateTime LoginTime { get; init; }
    public bool Active { get; init; } = true;
}

public sealed class LoginDto
{
    public Guid LoginId { get; init; }
    public Guid UserId { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public DateTime LoginTime { get; init; }
    public DateTime? LogoutTime { get; init; }
    public bool Active { get; init; }
}
