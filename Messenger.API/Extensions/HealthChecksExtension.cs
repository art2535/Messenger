using Messenger.Infrastructure.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
                services.AddHealthChecks()
                    .AddCheck("self", () => HealthCheckResult.Healthy("API is running"), tags: new[] { "live" })
                    .AddDbContextCheck<GuapMessengerContext>(
                        name: "postgresql",
                        failureStatus: HealthStatus.Unhealthy,
                        tags: new[] { "ready", "db" });

                return services;
            }
        }

        extension(WebApplication app)
        {
            public WebApplication MapMessengerHealthChecks()
            {
                app.MapHealthChecks("/health", new HealthCheckOptions
                {
                    ResponseWriter = WriteJsonResponse
                });

                app.MapHealthChecks("/health/live", new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains("live"),
                    ResponseWriter = WriteJsonResponse
                });

                app.MapHealthChecks("/health/ready", new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains("ready"),
                    ResponseWriter = WriteJsonResponse
                });

                return app;
            }
        }        

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
                        description = e.Value.Description,
                        duration = e.Value.Duration.ToString(),
                        error = e.Value.Exception?.Message
                    })
            };

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }),
                Encoding.UTF8);
        }
    }
}
