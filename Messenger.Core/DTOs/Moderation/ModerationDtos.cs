using System.ComponentModel.DataAnnotations;

namespace Messenger.Core.DTOs.Moderation
{
    public class ReportMessageRequest
    {
        [Required] public Guid MessageId { get; set; }
        [Required, MaxLength(64)] public string Reason { get; set; } = null!;
        [MaxLength(1000)] public string? Comment { get; set; }
    }

    public class MuteUserRequest
    {
        [Required] public Guid UserId { get; set; }
        public Guid? ChatId { get; set; }
        [Required] public int DurationMinutes { get; set; }
        [MaxLength(500)] public string? Reason { get; set; }
    }

    public class MessageReportDto
    {
        public Guid ReportId { get; set; }
        public Guid MessageId { get; set; }
        public Guid ChatId { get; set; }
        public Guid ReporterId { get; set; }
        public string? ReporterName { get; set; }
        public string Reason { get; set; } = null!;
        public string? Comment { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public string? MessagePreview { get; set; }
    }

    public class ModerationLogDto
    {
        public Guid LogId { get; set; }
        public Guid? ActorId { get; set; }
        public Guid? TargetUserId { get; set; }
        public Guid? ChatId { get; set; }
        public Guid? MessageId { get; set; }
        public string Action { get; set; } = null!;
        public string? Details { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class UserMuteDto
    {
        public Guid MuteId { get; set; }
        public Guid UserId { get; set; }
        public Guid? ChatId { get; set; }
        public DateTime MutedUntil { get; set; }
        public string? Reason { get; set; }
        public bool IsActive { get; set; }
    }
}
