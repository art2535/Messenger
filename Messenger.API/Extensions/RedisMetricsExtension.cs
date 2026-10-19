using Messenger.API.Services;
using StackExchange.Redis;

namespace Messenger.API.Extensions
{
    public static class RedisMetricsExtension
    {
        public const string MeterName = "Messenger.Redis";

        extension(IServiceCollection services)
        {
            public IServiceCollection AddRedisMetrics()
            {
                if (services.Any(d => d.ServiceType == typeof(IConnectionMultiplexer)))
                {
                    services.AddHostedService<RedisMetricsCollectorService>();
                }

                return services;
            }
        }
    }
}
