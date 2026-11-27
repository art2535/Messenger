using System.ComponentModel.DataAnnotations;

namespace Messenger.Application.DTOs.Messages
{
    public class UpdateMessageStatusRequest
    {
        [Required]
        public string Status { get; set; } = string.Empty;
    }
}
