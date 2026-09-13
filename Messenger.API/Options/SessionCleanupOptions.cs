namespace Messenger.API.Options
{
    public class SessionCleanupOptions
    {
        public const string SectionName = "SessionCleanup";
        public int IdleMinutes { get; set; } = 30;
        public int IntervalMinutes { get; set; } = 5;
    }
}
