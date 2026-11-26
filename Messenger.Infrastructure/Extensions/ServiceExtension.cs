using Messenger.Core.Interfaces;
using Messenger.Core.Services;
using Messenger.Infrastructure.Services;
using Messenger.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
                services.AddSingleton<DatabaseMigrationStatus>();
                services.AddHostedService<DatabaseMigrationHostedService>();
                services.AddLogRetention(configuration);

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
                services.AddScoped<IMessageExportService, MessageExportService>();
                services.AddScoped<IMessageSearchService, MessageSearchService>();
                services.AddScoped<IMessagePaginationService, MessagePaginationService>();
                services.AddScoped<IMessagePinningService, MessagePinningService>();
                services.AddScoped<IMessageMutationService, MessageMutationService>();
                services.AddScoped<IMessageCreationService, MessageCreationService>();
                services.AddScoped<IMessageReadService, MessageReadService>();
                services.AddScoped<IMessageLookupService, MessageLookupService>();
                services.AddScoped<IMessageEventPublisher, MessageEventPublisher>();
                services.AddScoped<IMessageSendingService, MessageSendingService>();
                services.AddScoped<IMessageForwardingService, MessageForwardingService>();
                services.AddScoped<IMessageRealtimeNotifier, MessageRealtimeNotifier>();
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

        }
    }
}
