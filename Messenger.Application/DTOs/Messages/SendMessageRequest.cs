using System.ComponentModel.DataAnnotations;

namespace Messenger.Application.DTOs.Messages
{
    public class SendMessageRequest
    {
        [Required]
        public Guid ChatId { get; set; }

        [Required]
        public Guid ReceiverId { get; set; }

        [Required]
        public string Content { get; set; } = string.Empty;
        public bool HasAttachments { get; set; }
    }
}
