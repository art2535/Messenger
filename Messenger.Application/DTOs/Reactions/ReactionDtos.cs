namespace Messenger.Application.DTOs.Reactions;

public sealed class CreateReactionDto
{
    public string ReactionType { get; init; } = string.Empty;
}

public sealed class ReactionDto
{
    public Guid ReactionId { get; init; }
    public Guid MessageId { get; init; }
    public Guid UserId { get; init; }
    public string ReactionType { get; init; } = string.Empty;
    public string? UserName { get; init; }
}
