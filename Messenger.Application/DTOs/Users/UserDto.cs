using System.Text.Json.Serialization;

namespace Messenger.Core.DTOs.Users;

/// <summary>
/// Прикладное представление пользователя без EF-навигаций.
/// </summary>
public sealed class UserDto
{
    public Guid UserId { get; init; }
    public string LastName { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public DateOnly RegistrationDate { get; init; }
    public string? Login { get; init; }
    public string? ExternalId { get; init; }

    [JsonIgnore]
    public AccountDto? Account { get; init; }

    [JsonIgnore]
    public IReadOnlyList<RoleDto> Roles { get; init; } = Array.Empty<RoleDto>();
}
