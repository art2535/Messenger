using Messenger.API.Extensions;
using Messenger.Core.Hubs;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Ui.Web.Extensions;
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

            builder.ConfigureDevelopmentCertificate();

            builder.AddMessengerSerilog("Messenger.API");
            builder.AddMessengerSerilogUi();
            builder.Services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddSimpleConsole(o =>
                {
                    o.SingleLine = true;
                    o.TimestampFormat = "HH:mm:ss ";
                });
            });

            builder.EnsureSharedDevelopmentEncryptionKey();
            builder.Services.AddControllers();
            builder.Services.AddValidation();
            builder.Services.AddSignalRService(builder.Configuration);
            builder.Services.AddRedisCache(builder.Configuration);
            builder.Services.AddScalarApi(builder.Configuration);
            builder.Services.AddPostgreSQL(builder.Configuration);
            builder.Services.AddServices();
            builder.Services.AddEncryption(builder.Configuration);
            builder.Services.AddRabbitMQ(builder.Configuration);
            builder.Services.AddHttpClient();
            builder.Services.AddEtaApiAuthentication(builder.Configuration, !builder.Environment.IsDevelopment());
            builder.Services.AddMessengerRateLimiting();
            builder.Services.AddSessionCleanup(builder.Configuration);
            builder.Services.AddLogRetention(builder.Configuration);
            builder.Services.AddMessengerHealthChecks();
            builder.Services.AddRedisMetrics();
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
                        .AddMeter("Messenger.Redis")
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

            app.UseSerilogRequestLogging(options =>
            {
                options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            });

            using (var migrationScope = app.Services.CreateScope())
            {
                var logger = migrationScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                var db = migrationScope.ServiceProvider.GetRequiredService<GuapMessengerContext>();

                try
                {
                    var appliedMigrations = db.Database.GetAppliedMigrations().ToList();
                    var currentMigration = appliedMigrations.LastOrDefault();

                    logger.LogInformation("Текущая миграция базы данных: {Migration}", currentMigration ?? "Нет");

                    var pendingMigrations = db.Database.GetPendingMigrations().ToList();

                    if (pendingMigrations.Count == 0)
                    {
                        logger.LogInformation("Нет ожидающих миграций для применения.");
                    }
                    else
                    {
                        logger.LogInformation("Найдено {Count} ожидающих миграций. Применяю...", pendingMigrations.Count);

                        foreach (var migration in pendingMigrations)
                        {
                            logger.LogInformation("Применяю миграцию: {Migration}", migration);
                            db.Database.Migrate(migration);
                            logger.LogInformation("Миграция успешно применена: {Migration}", migration);
                        }

                        logger.LogInformation("Все ожидающие миграции успешно применены.");
                    }

                    var finalMigration = db.Database
                        .GetAppliedMigrations()
                        .LastOrDefault();

                    logger.LogInformation("Миграция базы данных завершена. Текущая миграция: {Migration}",
                        finalMigration ?? "Нет");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка при применении миграций базы данных.");
                    throw;
                }
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

            if (app.Environment.IsDevelopment())
            {
                app.UseSerilogUi(options => options.WithRoutePrefix("serilog-ui"));
            }

            app.UseRateLimiter();
            app.MapControllers().RequireRateLimiting("api");

            app.MapHub<ChatHub>($"/api/v{apiVersion}/hubs/chat").RequireCors(WebCorsPolicy);
            app.MapHub<UserStatusHub>($"/api/v{apiVersion}/hubs/userstatus").RequireCors(WebCorsPolicy);
            app.MapHub<NotificationHub>($"/api/v{apiVersion}/hubs/notification").RequireCors(WebCorsPolicy);

            try
            {
                Log.Information("Запуск Messenger.API...");
                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Messenger.API неожиданно завершил работу");
                throw;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
