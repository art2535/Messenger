using Messenger.Core.Interfaces;
using Messenger.Core.Services;
using Messenger.Infrastructure.Interfaces.Chats;
using Messenger.Infrastructure.Services.Chats;
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
                services.AddHttpClient();
                services.AddEncryption(configuration);
                services.AddRabbitMQ(configuration);
                services.AddSingleton<DatabaseMigrationStatus>();
                services.AddHostedService<DatabaseMigrationHostedService>();
                services.AddHostedService<MessageNotificationOutboxDispatcher>();
                services.AddLogRetention(configuration);

                return services;
            }

            public IServiceCollection AddRedisCache(IConfiguration configuration)
            {
                var redisCs = configuration.GetConnectionString("Redis")
                    ?? configuration["ConnectionStrings:Redis"];

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
                services.AddScoped<IChatQueryWorkflowService, ChatQueryWorkflowService>();
                services.AddScoped<IChatManagementWorkflowService, ChatManagementWorkflowService>();
                services.AddScoped<IChatFolderService, ChatFolderService>();
                services.AddScoped<IChatFolderWorkflowService, ChatFolderWorkflowService>();
                services.AddScoped<INotificationService, NotificationService>();
                services.AddScoped<INotificationWorkflowService, NotificationWorkflowService>();
                services.AddScoped<IPushWorkflowService, PushWorkflowService>();
                services.AddScoped<IAdminWorkflowService, AdminWorkflowService>();
                services.AddScoped<IModerationWorkflowService, ModerationWorkflowService>();
                services.AddScoped<IAuthorizationWorkflowService, AuthorizationWorkflowService>();
                services.AddScoped<IBroadcastWorkflowService, BroadcastWorkflowService>();
                services.AddScoped<ILoginWorkflowService, LoginWorkflowService>();
                services.AddScoped<IOAuthProxyWorkflowService, OAuthProxyWorkflowService>();
                services.AddScoped<IUserStatusWorkflowService, UserStatusWorkflowService>();
                services.AddScoped<IUserWorkflowService, UserWorkflowService>();
                services.AddScoped<IAttachmentService, AttachmentService>();
                services.AddScoped<IAttachmentWorkflowService, AttachmentWorkflowService>();
                services.AddScoped<IReactionService, ReactionService>();
                services.AddScoped<IReactionWorkflowService, ReactionWorkflowService>();
                services.AddScoped<ILoginService, LoginService>();
                services.AddScoped<IAccountSettingsService, AccountSettingsService>();
                services.AddScoped<IAccountSettingsWorkflowService,
                    AccountSettingsWorkflowService>();
                services.AddScoped<IUserStatusService, UserStatusService>();
                services.AddScoped<IBroadcastService, BroadcastService>();
                services.AddScoped<IMessageService, MessageService>();
                services.AddScoped<MessageQueryFacade>();
                services.AddScoped<MessageCommandFacade>();
                services.AddScoped<MessageReadFacade>();
                services.AddScoped<MessageExportFacade>();
                services.AddScoped<IMessageExportService, MessageExportService>();
                services.AddScoped<IMessageChatExportService, MessageChatExportService>();
                services.AddScoped<IMessageSearchService, MessageSearchService>();
                services.AddScoped<IMessageSearchWorkflowService,
                    MessageSearchWorkflowService>();
                services.AddScoped<IMessagePaginationService, MessagePaginationService>();
                services.AddScoped<IMessagePinningService, MessagePinningService>();
                services.AddScoped<IMessageMutationService, MessageMutationService>();
                services.AddScoped<IMessageCreationService, MessageCreationService>();
                services.AddScoped<IMessageCreationWorkflowService,
                    MessageCreationWorkflowService>();
                services.AddScoped<IMessageReadService, MessageReadService>();
                services.AddScoped<IMessageLookupService, MessageLookupService>();
                services.AddScoped<IMessageEventPublisher, MessageEventPublisher>();
                services.AddScoped<IMessageSendingService, MessageSendingService>();
                services.AddScoped<IMessageForwardingService, MessageForwardingService>();
                services.AddScoped<IMessageEditingWorkflowService,
                    MessageEditingWorkflowService>();
                services.AddScoped<IMessageDeletionWorkflowService,
                    MessageDeletionWorkflowService>();
                services.AddScoped<IMessageControllerWorkflowService,
                    MessageControllerWorkflowService>();
                services.AddScoped<IScheduledMessageWorkflowService, ScheduledMessageWorkflowService>();
                services.AddScoped<IMessageHistoryWorkflowService,
                    MessageHistoryWorkflowService>();
                services.AddScoped<IMessageRealtimeNotifier, MessageRealtimeNotifier>();
                services.AddScoped<IAdminService, AdminService>();
                services.AddScoped<IMessageNotificationOutboxRecoveryService,
                    MessageNotificationOutboxRecoveryService>();
                services.AddScoped<IModerationService, ModerationService>();
                services.AddSingleton<WebPushClient>();
                services.AddScoped<IPushSubscriptionService, PushSubscriptionService>();
                services.AddSingleton<TypingRateLimiterService>();
                services.AddSingleton<SendMessageRateLimiterService>();
                services.AddScoped<ScheduledMessageService>();
                services.AddScoped<IScheduledMessageService>(sp =>
                    sp.GetRequiredService<ScheduledMessageService>());
                services.AddScoped<IScheduledMessageClaimService>(sp =>
                    sp.GetRequiredService<ScheduledMessageService>());

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
