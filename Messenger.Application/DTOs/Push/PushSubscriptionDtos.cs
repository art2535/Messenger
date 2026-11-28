namespace Messenger.Application.DTOs.Push;

public sealed class PushSubscriptionDto
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string Endpoint { get; init; } = string.Empty;
    public string P256dh { get; init; } = string.Empty;
    public string Auth { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
}

public sealed class CreatePushSubscriptionDto
{
    public string Endpoint { get; init; } = string.Empty;
    public string P256dh { get; init; } = string.Empty;
    public string Auth { get; init; } = string.Empty;
}

public sealed class UpdatePushSubscriptionDto
{
    public Guid Id { get; init; }
    public string Endpoint { get; init; } = string.Empty;
    public string P256dh { get; init; } = string.Empty;
    public string Auth { get; init; } = string.Empty;
    public DateTime LastUsedAt { get; init; }
}

public sealed class PushSettingsDto
{
    public bool PushEnabled { get; init; } = true;
    public bool NotifyMessages { get; init; } = true;
    public bool NotifyGroupChats { get; init; } = true;
    public bool NotifyMentions { get; init; } = true;
}

public sealed class UpdatePushSettingsDto
{
    public bool PushEnabled { get; init; } = true;
    public bool NotifyMessages { get; init; } = true;
    public bool NotifyGroupChats { get; init; } = true;
    public bool NotifyMentions { get; init; } = true;
}

public string? SenderName { get; init; }
    public string? MessageText { get; init; }
    public bool HasAttachments { get; init; }
    public bool IsMention { get; init; }
}
