namespace Messenger.Application.DTOs.UserStatuses;

public sealed class UpdateUserStatusDto
{
    public bool Online { get; init; }
}

public sealed class UserStatusDto
{
    public Guid UserId { get; init; }
    public bool Online { get; init; }
    public DateTime? LastActivity { get; init; }
}
