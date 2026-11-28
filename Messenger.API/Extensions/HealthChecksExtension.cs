using Messenger.Infrastructure.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;

namespace Messenger.API.Extensions
{
    public static class HealthChecksExtension
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddMessengerHealthChecks()
            {
                var builder = services.AddHealthChecks()
                    .AddCheck("self", () => HealthCheckResult.Healthy("API is running"), tags: new[] { "live" })
                    .AddDbContextCheck<GuapMessengerContext>(
                        name: "postgresql",
                        failureStatus: HealthStatus.Unhealthy,
                        tags: new[] { "ready", "db" });

                if (services.Any(d => d.ServiceType == typeof(IConnectionMultiplexer)))
                {
                    builder.AddCheck<RedisHealthCheck>(
                        name: "redis",
                        failureStatus: HealthStatus.Degraded,
                        tags: new[] { "ready", "cache" });
                }

                return services;
            }
        }

        extension(WebApplication app)
        {
            public WebApplication MapMessengerHealthChecks()
            {
                // Публичная liveness probe раскрывает только состояние процесса.
                app.MapHealthChecks("/health/live", new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains("live"),
                    ResponseWriter = WriteJsonResponse
                });

                // Readiness и агрегированный health раскрывают состояние PostgreSQL/Redis.
                app.MapHealthChecks("/health/ready", new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains("ready"),
                    ResponseWriter = WriteJsonResponse
                }).RequireAuthorization();

                app.MapHealthChecks("/health", new HealthCheckOptions
                {
                    ResponseWriter = WriteJsonResponse
                }).RequireAuthorization();

                return app;
            }
        }

        private static readonly JsonSerializerOptions HealthJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private static async Task WriteJsonResponse(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            var payload = new
            {
                status = report.Status.ToString(),
                totalDuration = report.TotalDuration.ToString(),
                entries = report.Entries.ToDictionary(
                    e => e.Key,
                    e => new
                    {
                        status = e.Value.Status.ToString(),
                        duration = e.Value.Duration.ToString()
                    })
            };

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(payload, HealthJsonOptions),
                Encoding.UTF8);
        }
    }
}