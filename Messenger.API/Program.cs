using Messenger.API.Extensions;
using Messenger.API.Services;
using Messenger.Core.Hubs;
using Messenger.Infrastructure.Extensions;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Ui.Web.Extensions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
                    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                });
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
                options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });
            builder.Services.AddValidation();
            builder.Services.AddSignalRService(builder.Configuration);
            builder.Services.AddScalarApi(builder.Configuration);
            builder.Services.AddMessengerInfrastructure(builder.Configuration);
            builder.Services.AddHttpClient();
            builder.Services.AddHostedService<ScheduledMessageDispatcherService>();
            builder.Services.AddEtaApiAuthentication(builder.Configuration, !builder.Environment.IsDevelopment());
            builder.Services.AddMessengerRateLimiting();
            builder.Services.AddSessionCleanup(builder.Configuration);
            builder.Services.AddMessengerHealthChecks();
            builder.Services.AddRedisMetrics();

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

            app.MapPrometheusScrapingEndpoint("/metrics");

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
