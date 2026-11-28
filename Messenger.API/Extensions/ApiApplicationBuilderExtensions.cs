using Messenger.API.Hubs;
using Messenger.Infrastructure.Data;
using Messenger.Realtime.Hubs;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using Serilog;
using Serilog.Ui.Web.Extensions;

namespace Messenger.API.Extensions;

/// <summary>
/// Содержит этапы запуска HTTP pipeline и публикации endpoint'ов Messenger.API.
/// </summary>
public static class ApiApplicationBuilderExtensions
{
    /// <summary>
    /// Применяет ожидающие миграции до начала обработки HTTP-запросов.
    /// </summary>
    public static void ApplyMessengerDatabaseMigrations(this WebApplication app)
    {
        using var migrationScope = app.Services.CreateScope();
        var logger = migrationScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var db = migrationScope.ServiceProvider.GetRequiredService<GuapMessengerContext>();

        try
        {
            var appliedMigrations = db.Database.GetAppliedMigrations().ToList();
            var currentMigration = appliedMigrations.LastOrDefault();

            logger.LogInformation(
                "Текущая миграция базы данных: {Migration}",
                currentMigration ?? "Нет");

            var pendingMigrations = db.Database.GetPendingMigrations().ToList();
            if (pendingMigrations.Count == 0)
            {
                logger.LogInformation("Нет ожидающих миграций для применения.");
            }
            else
            {
                logger.LogInformation(
                    "Найдено {Count} ожидающих миграций. Применяю...",
                    pendingMigrations.Count);

                foreach (var migration in pendingMigrations)
                {
                    logger.LogInformation("Применяю миграцию: {Migration}", migration);
                    db.Database.Migrate(migration);
                    logger.LogInformation("Миграция успешно применена: {Migration}", migration);
                }

                logger.LogInformation("Все ожидающие миграции успешно применены.");
            }

            var finalMigration = db.Database.GetAppliedMigrations().LastOrDefault();
            logger.LogInformation(
                "Миграция базы данных завершена. Текущая миграция: {Migration}",
                finalMigration ?? "Нет");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при применении миграций базы данных.");
            throw;
        }
    }

    /// <summary>
    /// Подключает request logging и извлечение SignalR access token до маршрутизации.
    /// </summary>
    public static WebApplication UseMessengerApiRequestPipeline(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        });

        app.UseHubAccessToken();
        return app;
    }

    /// <summary>
    /// Подключает оставшиеся middleware в исходном порядке.
    /// </summary>
    public static WebApplication UseMessengerApiPipeline(this WebApplication app)
    {
        app.UseStaticAssets();
        app.UseCors(ApiServiceCollectionExtensions.WebCorsPolicy);
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.UseSerilogUi(options => options.WithRoutePrefix("serilog-ui"));
        }

        app.UseRateLimiter();

        return app;
    }

    /// <summary>
    /// Публикует HTTP, health, metrics, Scalar и SignalR endpoint'ы.
    /// </summary>
    public static WebApplication MapMessengerDiagnosticsEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapScalarApi(app.Configuration);
        }

        app.MapMessengerHealthChecks();

        // Prometheus scrape не использует пользовательский JWT.
        // Ограничивайте доступ к /metrics внутренней сетью или reverse proxy.
        app.MapPrometheusScrapingEndpoint("/metrics");
        return app;
    }

    public static WebApplication MapMessengerApiEndpoints(this WebApplication app)
    {
        app.MapControllers().RequireRateLimiting("api");

        var apiVersion = app.Configuration["URL:API:Version"] ?? "1.0";

        app.MapHub<ChatHub>($"/api/v{apiVersion}/hubs/chat")
            .RequireCors(ApiServiceCollectionExtensions.WebCorsPolicy);
        app.MapHub<UserStatusHub>($"/api/v{apiVersion}/hubs/userstatus")
            .RequireCors(ApiServiceCollectionExtensions.WebCorsPolicy);
        app.MapHub<NotificationHub>($"/api/v{apiVersion}/hubs/notification")
            .RequireCors(ApiServiceCollectionExtensions.WebCorsPolicy);

        return app;
    }

    private static void UseHubAccessToken(this WebApplication app)
    {
        var apiVersion = app.Configuration["URL:API:Version"] ?? "1.0";

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments($"/api/v{apiVersion}/hubs/chat") ||
                context.Request.Path.StartsWithSegments($"/api/v{apiVersion}/hubs/userstatus") ||
                context.Request.Path.StartsWithSegments($"/api/v{apiVersion}/hubs/notification"))
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    (context.Request.Headers.Authorization.Count == 0 ||
                     !context.Request.Headers.Authorization.ToString()
                         .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)))
                {
                    context.Request.Headers.Authorization = $"Bearer {accessToken}";
                }
            }

            await next();
        });
    }
}
