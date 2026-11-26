using Messenger.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Messenger.Infrastructure.Persistence
{
    public static class DatabaseHealthChecksExtension
    {
        extension(IHealthChecksBuilder builder)
        {
            public IHealthChecksBuilder AddMessengerDatabaseHealthChecks()
            {
                return builder
                    .AddCheck<DatabaseMigrationHealthCheck>("database-migrations", tags: new[] { "ready", "db" })
                    .AddDbContextCheck<GuapMessengerContext>(
                        name: "postgresql",
                        failureStatus: HealthStatus.Unhealthy,
                        tags: new[] { "ready", "db" });
            }
        }
    }
}
