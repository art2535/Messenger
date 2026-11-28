using System.ComponentModel.DataAnnotations;

namespace Messenger.API.Options
{
    public class SessionCleanupOptions
    {
        public const string SectionName = "SessionCleanup";
        [Range(5, 43200)]
        public int IdleMinutes { get; set; } = 30;
        [Range(1, 1440)]
        public int IntervalMinutes { get; set; } = 5;
    }
}
