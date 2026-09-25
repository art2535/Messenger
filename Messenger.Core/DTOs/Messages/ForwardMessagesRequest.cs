using System.ComponentModel.DataAnnotations;

namespace Messenger.Core.DTOs.Messages
{
    public class ForwardMessagesRequest
    {
        [Required]
        public List<Guid> MessageIds { get; set; } = new();

        [Required]
        public List<Guid> TargetChatIds { get; set; } = new();
    }
}
