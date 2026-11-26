using MassTransit;
using Messenger.Core.Interfaces;
using Messenger.Core.Services;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Messaging.Consumers;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;
using WebPush;

namespace Messenger.Infrastructure.Extensions
{
    public static class ServiceExtension
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddMessengerInfrastructure(IConfiguration configuration)
            {
                services.AddPostgreSQL(configuration);
                services.AddRedisCache(configuration);
                services.AddServices();
                services.AddEncryption(configuration);
                services.AddRabbitMQ(configuration);

                return services;
            }

            public IServiceCollection AddPostgreSQL(IConfiguration? configuration = null)
            {
                var connectionString = Environment.GetEnvironmentVariable("PostgresConnectionString",
                    EnvironmentVariableTarget.Machine) ?? string.Empty;

                if (string.IsNullOrEmpty(connectionString))
                {
                    if (configuration is null)
                    {
                        throw new InvalidOperationException(
                            "Не указана строка подключения к PostgreSQL. Добавьте переменную окружения " +
                            "'PostgresConnectionString' или настройте 'DefaultConnection' в appsettings.json.");
                    }

                    connectionString = configuration.GetConnectionString("DefaultConnection");
                }

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException("Строка подключения PostgreSQL пуста после всех попыток чтения.");
                }

                connectionString = EnsurePoolSettings(connectionString);

                var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
                var dataSource = dataSourceBuilder.Build();

                services.AddSingleton(dataSource);
                services.AddDbContext<GuapMessengerContext>((sp, options) =>
                {
                    var ds = sp.GetRequiredService<NpgsqlDataSource>();
                    options.UseNpgsql(ds, npgsql => npgsql.CommandTimeout(15));
                });

                return services;
            }

            public IServiceCollection AddRedisCache(IConfiguration configuration)
            {
                var redisCs = configuration.GetConnectionString("Redis") ?? configuration["ConnectionStrings:Redis"];

                if (!string.IsNullOrWhiteSpace(redisCs))
                {
                    services.AddSingleton<IConnectionMultiplexer>(_ =>
                    {
                        var options = ConfigurationOptions.Parse(redisCs);
                        options.AbortOnConnectFail = false;
                        options.AllowAdmin = true;
                        return ConnectionMultiplexer.Connect(options);
                    });

                    services.AddSingleton<ICacheService>(sp =>
                    {
                        var mux = sp.GetRequiredService<IConnectionMultiplexer>();
                        var logger = sp.GetRequiredService<ILogger<RedisCacheService>>();
                        return new RedisCacheService(mux, logger);
                    });

                    services.AddSingleton<RedisDistributedLock>(sp =>
                    {
                        var mux = sp.GetRequiredService<IConnectionMultiplexer>();
                        var logger = sp.GetRequiredService<ILogger<RedisDistributedLock>>();
                        return new RedisDistributedLock(mux, logger);
                    });
                }
                else
                {
                    services.AddSingleton<ICacheService>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<RedisCacheService>>();
                        return new RedisCacheService(null, logger);
                    });

                    services.AddSingleton<RedisDistributedLock>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<RedisDistributedLock>>();
                        return new RedisDistributedLock(null, logger);
                    });
                }

                return services;
            }

            public IServiceCollection AddServices()
            {
                services.AddScoped<IUserService, UserService>();
                services.AddScoped<IChatService, ChatService>();
                services.AddScoped<IChatFolderService, ChatFolderService>();
                services.AddScoped<INotificationService, NotificationService>();
                services.AddScoped<IAttachmentService, AttachmentService>();
                services.AddScoped<IReactionService, ReactionService>();
                services.AddScoped<ILoginService, LoginService>();
                services.AddScoped<IAccountSettingsService, AccountSettingsService>();
                services.AddScoped<IUserStatusService, UserStatusService>();
                services.AddScoped<IBroadcastService, BroadcastService>();
                services.AddScoped<IMessageService, MessageService>();
                services.AddScoped<IAdminService, AdminService>();
                services.AddScoped<IModerationService, ModerationService>();
                services.AddSingleton<WebPushClient>();
                services.AddScoped<IPushSubscriptionService, PushSubscriptionService>();
                services.AddSingleton<TypingRateLimiterService>();
                services.AddSingleton<SendMessageRateLimiterService>();
                services.AddScoped<IScheduledMessageService, ScheduledMessageService>();

                return services;
            }

            public IServiceCollection AddEncryption(IConfiguration configuration)
            {
                services.Configure<AesGcmEncryptionOptions>(configuration.GetSection("Encryption"));
                services.AddSingleton<IEncryptionService, AesGcmEncryptionService>();

                return services;
            }

            public IServiceCollection AddRabbitMQ(IConfiguration configuration)
            {
                services.AddMassTransit(x =>
                {
                    x.SetKebabCaseEndpointNameFormatter();

                    x.AddEntityFrameworkOutbox<GuapMessengerContext>(o =>
                    {
                        o.UsePostgres();
                        o.UseBusOutbox(b =>
                        {
                            b.MessageDeliveryLimit = 100;
                            b.MessageDeliveryTimeout = TimeSpan.FromSeconds(30);
                        });

                        o.QueryDelay = TimeSpan.FromSeconds(3);
                        o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
                    });

                    x.AddConsumer<ChatMessageSentConsumer>();

                    x.AddConfigureEndpointsCallback((_, cfg) =>
                    {
                        if (cfg is IRabbitMqReceiveEndpointConfigurator rmq)
                            rmq.SetQuorumQueue(1);
                    });

                    x.UsingRabbitMq((context, cfg) =>
                    {
                        var connectionString = configuration.GetConnectionString("RabbitMQ") ??
                            configuration["ConnectionStrings:RabbitMQ"];

                        if (string.IsNullOrWhiteSpace(connectionString))
                        {
                            throw new InvalidOperationException(
                                "Не задана строка подключения RabbitMQ. Укажите ConnectionStrings:RabbitMQ " +
                                "(например amqp://guest:guest@localhost:5672) или передайте её через Aspire / " +
                                "user-secrets / env.");
                        }

                        cfg.Host(new Uri(connectionString));
                        cfg.UseMessageRetry(r => r.Incremental(5, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)));
                        cfg.UseConcurrencyLimit(30);
                        cfg.PrefetchCount = 32;
                        cfg.ConfigureEndpoints(context);
                    });
                });

                return services;
            }

            public WebApplication InitializeMessengerDatabase()
            {
                var app = this;
                using var migrationScope = app.Services.CreateScope();
                var logger = migrationScope.ServiceProvider.GetRequiredService<ILogger<WebApplication>>();
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

                    var finalMigration = db.Database.GetAppliedMigrations().LastOrDefault();
                    logger.LogInformation("Миграция базы данных завершена. Текущая миграция: {Migration}",
                        finalMigration ?? "Нет");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка при применении миграций базы данных.");
                    throw;
                }

                return app;
            }
        }

        private static string EnsurePoolSettings(string connectionString)
        {
            var csb = new NpgsqlConnectionStringBuilder(connectionString);

            if (!connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase))
                csb.MaxPoolSize = 100;

            if (!connectionString.Contains("Minimum Pool Size", StringComparison.OrdinalIgnoreCase) &&
                !connectionString.Contains("Min Pool Size", StringComparison.OrdinalIgnoreCase))
                csb.MinPoolSize = 5;

            return csb.ConnectionString;
        }
    }
}
