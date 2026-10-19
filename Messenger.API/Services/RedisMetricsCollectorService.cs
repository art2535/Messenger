using System.Diagnostics.Metrics;
using Messenger.API.Extensions;
using StackExchange.Redis;

namespace Messenger.API.Services
{
    public sealed class RedisMetricsCollectorService : BackgroundService
    {
        private static readonly Meter Meter = new(RedisMetricsExtension.MeterName, "1.0.0");

        private readonly IConnectionMultiplexer _mux;
        private readonly ILogger<RedisMetricsCollectorService> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(15);

        private readonly ObservableGauge<long> _connectedClients;
        private readonly ObservableGauge<long> _usedMemoryBytes;
        private readonly ObservableGauge<long> _usedMemoryPeakBytes;
        private readonly ObservableGauge<long> _keysTotal;
        private readonly ObservableGauge<long> _expiredKeys;
        private readonly ObservableGauge<long> _evictedKeys;
        private readonly ObservableGauge<long> _opsPerSec;
        private readonly ObservableGauge<long> _hitRatePercent;
        private readonly ObservableGauge<long> _connected;
        private readonly ObservableGauge<double> _pingMs;

        private long _connectedClientsValue;
        private long _usedMemoryBytesValue;
        private long _usedMemoryPeakBytesValue;
        private long _keysTotalValue;
        private long _expiredKeysValue;
        private long _evictedKeysValue;
        private long _opsPerSecValue;
        private long _hitRatePercentValue;
        private long _connectedValue;
        private double _pingMsValue;

        public RedisMetricsCollectorService(IConnectionMultiplexer mux, ILogger<RedisMetricsCollectorService> logger)
        {
            _mux = mux;
            _logger = logger;

            _connectedClients = Meter.CreateObservableGauge(
                "messenger.redis.connected_clients",
                () => _connectedClientsValue,
                unit: "{client}",
                description: "Number of client connections to Redis");

            _usedMemoryBytes = Meter.CreateObservableGauge(
                "messenger.redis.used_memory_bytes",
                () => _usedMemoryBytesValue,
                unit: "By",
                description: "Memory used by Redis");

            _usedMemoryPeakBytes = Meter.CreateObservableGauge(
                "messenger.redis.used_memory_peak_bytes",
                () => _usedMemoryPeakBytesValue,
                unit: "By",
                description: "Peak memory used by Redis");

            _keysTotal = Meter.CreateObservableGauge(
                "messenger.redis.keys",
                () => _keysTotalValue,
                unit: "{key}",
                description: "Total keys across all databases (db0..N keyspace)");

            _expiredKeys = Meter.CreateObservableGauge(
                "messenger.redis.expired_keys",
                () => _expiredKeysValue,
                unit: "{key}",
                description: "Total number of key expiration events");

            _evictedKeys = Meter.CreateObservableGauge(
                "messenger.redis.evicted_keys",
                () => _evictedKeysValue,
                unit: "{key}",
                description: "Total number of evicted keys due to maxmemory");

            _opsPerSec = Meter.CreateObservableGauge(
                "messenger.redis.instantaneous_ops_per_sec",
                () => _opsPerSecValue,
                unit: "{ops}/s",
                description: "Number of commands processed per second");

            _hitRatePercent = Meter.CreateObservableGauge(
                "messenger.redis.keyspace_hit_rate_percent",
                () => _hitRatePercentValue,
                unit: "%",
                description: "Keyspace hit rate percentage (hits / (hits+misses) * 100)");

            _connected = Meter.CreateObservableGauge(
                "messenger.redis.connected",
                () => _connectedValue,
                unit: "{bool}",
                description: "1 if multiplexer is connected, else 0");

            _pingMs = Meter.CreateObservableGauge(
                "messenger.redis.ping_ms",
                () => _pingMsValue,
                unit: "ms",
                description: "Redis PING latency in milliseconds");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CollectAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Redis metrics collection failed");
                    _connectedValue = 0;
                }

                try
                {
                    await Task.Delay(_interval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task CollectAsync(CancellationToken ct)
        {
            _connectedValue = _mux.IsConnected ? 1 : 0;

            if (!_mux.IsConnected)
                return;

            var db = _mux.GetDatabase();
            var ping = await db.PingAsync();
            _pingMsValue = ping.TotalMilliseconds;

            var endpoints = _mux.GetEndPoints();
            if (endpoints.Length == 0)
                return;

            var server = _mux.GetServer(endpoints[0]);
            if (!server.IsConnected)
            {
                _connectedValue = 0;
                return;
            }

            IGrouping<string, KeyValuePair<string, string>>[] info;
            try
            {
                info = await server.InfoAsync();
            }
            catch (RedisCommandException ex) when (ex.Message.Contains("admin mode", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Redis INFO недоступен: включи AllowAdmin=true в ConfigurationOptions (или allowAdmin=true в connection string)");
                return;
            }

            long hits = 0, misses = 0;
            long keysSum = 0;

            foreach (var section in info)
            {
                foreach (var entry in section)
                {
                    switch (entry.Key)
                    {
                        case "connected_clients":
                            _connectedClientsValue = ParseLong(entry.Value);
                            break;
                        case "used_memory":
                            _usedMemoryBytesValue = ParseLong(entry.Value);
                            break;
                        case "used_memory_peak":
                            _usedMemoryPeakBytesValue = ParseLong(entry.Value);
                            break;
                        case "expired_keys":
                            _expiredKeysValue = ParseLong(entry.Value);
                            break;
                        case "evicted_keys":
                            _evictedKeysValue = ParseLong(entry.Value);
                            break;
                        case "instantaneous_ops_per_sec":
                            _opsPerSecValue = ParseLong(entry.Value);
                            break;
                        case "keyspace_hits":
                            hits = ParseLong(entry.Value);
                            break;
                        case "keyspace_misses":
                            misses = ParseLong(entry.Value);
                            break;
                        default:
                            if (entry.Key.StartsWith("db", StringComparison.Ordinal)
                                && entry.Value.Contains("keys=", StringComparison.Ordinal))
                            {
                                keysSum += ParseKeyspaceKeys(entry.Value);
                            }
                            break;
                    }
                }
            }

            _keysTotalValue = keysSum;
            var total = hits + misses;
            _hitRatePercentValue = total > 0 ? (long)Math.Round(100.0 * hits / total) : 100;
        }

        private static long ParseLong(string? value)
        {
            return long.TryParse(value, out var n) ? n : 0;
        }

        private static long ParseKeyspaceKeys(string value)
        {
            const string prefix = "keys=";
            var idx = value.IndexOf(prefix, StringComparison.Ordinal);
            if (idx < 0) return 0;
            idx += prefix.Length;
            var end = value.IndexOf(',', idx);
            var num = end < 0 ? value[idx..] : value[idx..end];
            return long.TryParse(num, out var n) ? n : 0;
        }
    }
}