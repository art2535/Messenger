using Messenger.API.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Messenger.API.Extensions;

/// <summary>
/// Собирает регистрации зависимостей и параметры инфраструктуры HTTP API.
/// </summary>
public static class ApiServiceCollectionExtensions
{
    internal const string WebCorsPolicy = "WebClient";

    /// <summary>
    /// Регистрирует все сервисы, необходимые Messenger.API.
    /// </summary>
    public static WebApplicationBuilder AddMessengerApiLogging(this WebApplicationBuilder builder)
    {
        ConfigureLogging(builder);
        return builder;
    }

    public static WebApplicationBuilder AddMessengerApiServices(this WebApplicationBuilder builder)
    {
        ConfigureJson(builder.Services);

        builder.Services.AddValidation();
        builder.Services.AddSignalRService(builder.Configuration);
        builder.Services.AddRedisCache(builder.Configuration);
        builder.Services.AddScalarApi(builder.Configuration);
        builder.Services.AddPostgreSQL(builder.Configuration);
        builder.Services.AddServices();
        builder.Services.AddEncryption(builder.Configuration);
        builder.Services.AddRabbitMQ(builder.Configuration);
        builder.Services.AddHttpClient();

        RegisterHostedServices(builder.Services);
        ConfigureAuthenticationAndSecurity(builder);
        RegisterBackgroundMaintenance(builder.Services, builder.Configuration);
        ConfigureCors(builder.Services);
        ConfigureOpenTelemetry(builder.Services);

        return builder;
    }

    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        builder.AddMessengerSerilog("Messenger.API");
        builder.AddMessengerSerilogUi();

        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        });
    }

    private static void ConfigureJson(IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
    }

    private static void RegisterHostedServices(IServiceCollection services)
    {
        services.AddHostedService<ScheduledMessageDispatcherService>();
        services.AddHostedService<UploadOrphanCleanupService>();
    }

    private static void RegisterBackgroundMaintenance(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSessionCleanup(configuration);
        services.AddLogRetention(configuration);
        services.AddMessengerHealthChecks();
        services.AddRedisMetrics();
    }

    private static void ConfigureAuthenticationAndSecurity(WebApplicationBuilder builder)
    {
        builder.Services.AddEtaApiAuthentication(
            builder.Configuration,
            !builder.Environment.IsDevelopment());

        builder.Services.AddMessengerRateLimiting();
    }

    private static void ConfigureCors(IServiceCollection services)
    {
        services.AddCors(options =>
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
    }

    private static void ConfigureOpenTelemetry(IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
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
    }
}
