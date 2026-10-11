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

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (_multiplexer.IsConnected)
                return Task.FromResult(HealthCheckResult.Healthy("Redis доступен"));

            return Task.FromResult(HealthCheckResult.Degraded(
                "Redis недоступен: запросы идут напрямую в PostgreSQL (без кэша)"));
        }
    }
}