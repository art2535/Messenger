namespace Messenger.Core.Models
{
    public static class UtcDateTime
    {
        public static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }
}