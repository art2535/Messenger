using System.ComponentModel.DataAnnotations;
using Messenger.Core.Messages;

namespace Messenger.Core.DTOs.Messages;

public class ScheduleMessageRequest
{
    /// <summary>Текст сообщения (plaintext; сервер зашифрует).</summary>
    public string? MessageText { get; set; }

    /// <summary>UTC или с offset; сервер нормализует в UTC.</summary>
    [Required]
    public DateTimeOffset ScheduledAt { get; set; }

    /// <summary>Опционально reply-to message id.</summary>
    public string? ReplyToMessageId { get; set; }
}

public class UpdateScheduledMessageRequest
{
    public string? MessageText { get; set; }

    public DateTimeOffset? ScheduledAt { get; set; }
}

public class ScheduledMessageDto
{
    public Guid ScheduledMessageId { get; set; }
    public Guid ChatId { get; set; }
    public Guid SenderId { get; set; }
    public string? SenderName { get; set; }
    public string? MessageText { get; set; }
    public bool HasAttachments { get; set; }
    public List<AttachmentDto> Attachments { get; set; } = new();
    public DateTime ScheduledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public Guid? ResultMessageId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ReplyToMessageId { get; set; }
    public string? ChatName { get; set; }
}

public class ScheduleMessageSuccessResponse
{
    public bool IsSuccess { get; set; } = true;
    public ScheduledMessageDto? Data { get; set; }
}

public class ScheduledMessagesListResponse
{
    public bool IsSuccess { get; set; } = true;
    public List<ScheduledMessageDto> Data { get; set; } = new();
}
