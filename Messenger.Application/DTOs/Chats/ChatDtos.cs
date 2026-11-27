using System.ComponentModel.DataAnnotations;

namespace Messenger.Application.DTOs.Chats;

public sealed class CreateChatDto
{
    public string? Name { get; init; } = string.Empty;

    [Required]
    public string Type { get; init; } = string.Empty;

    [Required]
    public List<Guid> UserIds { get; init; } = new();
}

public sealed class UpdateChatDto
{
    [Required]
    public string Name { get; init; } = string.Empty;
}

public sealed class ChatParticipantDto
{
    public Guid ChatId { get; init; }
    public Guid UserId { get; init; }
    public string? Role { get; init; }
    public DateTime JoinDate { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Avatar { get; init; }
    public string? ExternalId { get; init; }
}

public sealed class ChatDto
{
    public Guid ChatId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public DateTime CreationDate { get; init; }
    public IReadOnlyList<ChatParticipantDto> Participants { get; init; } = Array.Empty<ChatParticipantDto>();
}

public sealed class ChatListItemDto
{
    public Guid ChatId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Avatar { get; init; }
    public string Type { get; init; } = string.Empty;
    public string? LastMessage { get; init; }
    public DateTime? LastMessageAt { get; init; }
    public DateTime? LastMessageTime { get; init; }
    public DateTime? LastMessageSentAt { get; init; }
    public bool IsOnline { get; init; }
    public int UnreadCount { get; init; }
}