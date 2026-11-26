using System.Diagnostics.Metrics;

namespace Messenger.Infrastructure.Services
{
    public static class CacheMetrics
    {
        public static readonly Meter Meter = new("Messenger.Cache", "1.0.0");

        public static readonly Counter<long> Hits =
            Meter.CreateCounter<long>("messenger.cache.hits", unit: "{hit}", description: "Cache hits");

        public static readonly Counter<long> Misses =
            Meter.CreateCounter<long>("messenger.cache.misses", unit: "{miss}", description: "Cache misses");

        public static readonly Counter<long> Errors =
            Meter.CreateCounter<long>("messenger.cache.errors", unit: "{error}", description: "Cache errors / fallbacks");

        public static void RecordHit(string? keyPrefix = null) =>
            Hits.Add(1, new KeyValuePair<string, object?>("prefix", keyPrefix ?? "unknown"));

        public static void RecordMiss(string? keyPrefix = null) =>
            Misses.Add(1, new KeyValuePair<string, object?>("prefix", keyPrefix ?? "unknown"));

        public static void RecordError(string? keyPrefix = null) =>
            Errors.Add(1, new KeyValuePair<string, object?>("prefix", keyPrefix ?? "unknown"));

        private static string? PrefixOf(string key)
        {
            var idx = key.IndexOf(':');
            return idx > 0 ? key[..idx] : key;
        }

        public static void RecordHitForKey(string key) => RecordHit(PrefixOf(key));
        public static void RecordMissForKey(string key) => RecordMiss(PrefixOf(key));
        public static void RecordErrorForKey(string key) => RecordError(PrefixOf(key));
    }
}
