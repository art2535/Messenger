using Messenger.API.Extensions;
using Messenger.Core.Hubs;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System.Text;

namespace Messenger.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (Console.IsOutputRedirected)
            {
                Console.OutputEncoding = Encoding.UTF8;
            }

            var builder = WebApplication.CreateBuilder(args);

            Console.WriteLine("=============================================================");
            Console.WriteLine($"[API DEBUG] Среда: {builder.Environment.EnvironmentName}");
            Console.WriteLine($"[API DEBUG] ASPNETCORE_URLS: '{builder.Configuration["ASPNETCORE_URLS"]}'");
            Console.WriteLine($"[API DEBUG] ASPNETCORE_HTTPS_PORTS: '{builder.Configuration["ASPNETCORE_HTTPS_PORTS"]}'");
            Console.WriteLine("=============================================================");

            builder.ConfigureDevelopmentCertificate();

            builder.EnsureSharedDevelopmentEncryptionKey();
            builder.Services.AddControllers();
            builder.Services.AddValidation();
            builder.Services.AddSignalRService(builder.Configuration);
            builder.Services.AddRedisCache(builder.Configuration);
            builder.Services.AddScalarApi(builder.Configuration);
            builder.Services.AddPostgreSQL(builder.Configuration);
            builder.Services.AddServices();
            builder.Services.AddEncryption(builder.Configuration);
            builder.Services.AddLogging();
            builder.Services.AddRabbitMQ(builder.Configuration);
            builder.Services.AddHttpClient();
            builder.Services.AddEtaApiAuthentication(builder.Configuration, !builder.Environment.IsDevelopment());
            builder.Services.AddMessengerRateLimiting();
            builder.Services.AddSessionCleanup(builder.Configuration);
            builder.Services.AddMessengerHealthChecks();
            builder.Services.AddMetricsProtection(builder.Configuration);

            builder.Services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService(
                    serviceName: "Messenger.API",
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"))
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddMeter("Microsoft.EntityFrameworkCore")
                        .AddMeter("MassTransit")
                        .AddMeter("Messenger")
                        .AddPrometheusExporter();
                });

            const string WebCorsPolicy = "WebClient";

            builder.Services.AddCors(options =>
            {
                options.AddPolicy(WebCorsPolicy, policy =>
                {
                    policy.SetIsOriginAllowed(origin =>
                    {
                        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                        {
                            return false;
                        }

                        return uri.Host == "localhost" ||
                                uri.Host.EndsWith(".dev.localhost", StringComparison.OrdinalIgnoreCase);
                    })
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
                });
            });

            var app = builder.Build();

            using (var migrationScope = app.Services.CreateScope())
            {
                var db = migrationScope.ServiceProvider.GetRequiredService<GuapMessengerContext>();
                db.Database.Migrate();
            }

            if (app.Environment.IsDevelopment())
            {
                app.MapScalarApi(app.Configuration);
            }

            var apiVersion = builder.Configuration["URL:API:Version"] ?? "1.0";

            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments($"/api/v{apiVersion}/hubs/chat") ||
                    context.Request.Path.StartsWithSegments($"/api/v{apiVersion}/hubs/userstatus") ||
                    context.Request.Path.StartsWithSegments($"/api/v{apiVersion}/hubs/notification"))
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) &&
                        (context.Request.Headers.Authorization.Count == 0 ||
                         !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)))
                    {
                        context.Request.Headers.Authorization = $"Bearer {accessToken}";
                    }
                }

                await next();
            });

            app.MapMessengerHealthChecks();

            app.MapProtectedPrometheusScrapingEndpoint("/metrics");

            app.UseUploads();
            app.UseCors(WebCorsPolicy);
            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.MapControllers().RequireRateLimiting("api");

            app.MapHub<ChatHub>($"/api/v{apiVersion}/hubs/chat").RequireCors(WebCorsPolicy);
            app.MapHub<UserStatusHub>($"/api/v{apiVersion}/hubs/userstatus").RequireCors(WebCorsPolicy);
            app.MapHub<NotificationHub>($"/api/v{apiVersion}/hubs/notification").RequireCors(WebCorsPolicy);

            app.Run();
        }
    }
}
