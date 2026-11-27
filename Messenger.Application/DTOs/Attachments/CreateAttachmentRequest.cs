using System.ComponentModel.DataAnnotations;

namespace Messenger.Application.DTOs.Attachments
{
    public class CreateAttachmentRequest
    {

        [Required]
        public string FileName { get; set; } = string.Empty;

        [Required]
        public string FileType { get; set; } = string.Empty;

        [Required]
        public int SizeInBytes { get; set; }

        [Required]
        public string Url { get; set; } = string.Empty;
    }
}
