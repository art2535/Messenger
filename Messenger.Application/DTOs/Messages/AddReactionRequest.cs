using System.ComponentModel.DataAnnotations;

namespace Messenger.Application.DTOs.Messages
{
    public class AddReactionRequest
    {
        [Required]
        public string ReactionType { get; set; } = string.Empty;
    }
}
