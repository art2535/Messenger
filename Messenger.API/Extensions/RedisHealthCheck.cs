using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Messenger.API.Extensions
{
    public sealed class RedisHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer _multiplexer;

        public RedisHealthCheck(IConnectionMultiplexer multiplexer)
        {
            _multiplexer = multiplexer;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!_multiplexer.IsConnected)
                {
                    return HealthCheckResult.Degraded(
                        "Redis недоступен: запросы идут напрямую в PostgreSQL (без кэша)");
                }

                var db = _multiplexer.GetDatabase();
                var pong = await db.PingAsync();

                var data = new Dictionary<string, object>
                {
                    ["latencyMs"] = pong.TotalMilliseconds,
                    ["cacheHits"] = "see meter Messenger.Cache / messenger.cache.hits",
                    ["cacheMisses"] = "see meter Messenger.Cache / messenger.cache.misses"
                };

                return HealthCheckResult.Healthy(
                    $"Redis доступен (ping {pong.TotalMilliseconds:F1} ms)", data);
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Degraded(
                    "Redis ошибка: запросы идут напрямую в PostgreSQL (без кэша)",
                    exception: ex);
            }
        }
    }
}
