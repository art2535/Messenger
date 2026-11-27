namespace Messenger.Core.DTOs.Users;

/// <summary>
/// Прикладное представление роли без EF-навигаций.
/// </summary>
public sealed class RoleDto
{
    public Guid RoleId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
