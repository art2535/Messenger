namespace Messenger.Core.Models
{
    public static class UtcDateTime
    {
        public static DateTime Now => DateTime.UtcNow;

        public static DateTime AsUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        public static DateTime? AsUtc(DateTime? value)
            => value.HasValue ? AsUtc(value.Value) : null;
    }
}
