using Messenger.API.Options;
using Messenger.API.Providers;
using Messenger.API.Services;
using Messenger.Core.Interfaces;
using Messenger.Core.Services;
using Messenger.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using WebPush;

namespace Messenger.API.Extensions
{
    public static class ServiceExtension
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddServices()
            {
                services.AddScoped<IUserService, UserService>();
                services.AddScoped<IChatService, ChatService>();
                services.AddScoped<INotificationService, NotificationService>();
                services.AddScoped<IAttachmentService, AttachmentService>();
                services.AddScoped<IReactionService, ReactionService>();
                services.AddScoped<ILoginService, LoginService>();
                services.AddScoped<IUserStatusService, UserStatusService>();
                services.AddScoped<IBroadcastService, BroadcastService>();
                services.AddScoped<IMessageService, MessageService>();
                services.AddSingleton<WebPushClient>();
                services.AddScoped<IPushSubscriptionService, PushSubscriptionService>();
                services.AddSingleton<TypingRateLimiterService>();

                return services;
            }

            public IServiceCollection AddSessionCleanup(IConfiguration configuration)
            {
                services.Configure<SessionCleanupOptions>(configuration.GetSection(SessionCleanupOptions.SectionName));
                services.AddHostedService<SessionCleanupBackgroundService>();
                return services;
            }

            public IServiceCollection AddSignalRService(IConfiguration configuration)
            {
                var redisCs = configuration["Redis:ConnectionString"];
                var channelPrefix = configuration["Redis:ChannelPrefix"] ?? "Messenger";

                var signalR = services.AddSignalR(options =>
                {
                    options.MaximumReceiveMessageSize = 64 * 1024;
                    options.EnableDetailedErrors = configuration.GetValue("SignalR:EnableDetailedErrors", false);
                    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
                    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                });

                if (!string.IsNullOrWhiteSpace(redisCs))
                {
                    signalR.AddStackExchangeRedis(redisCs, options =>
                    {
                        options.Configuration.ChannelPrefix = RedisChannel.Literal(channelPrefix);
                    });
                }

                services.AddSingleton<IUserIdProvider, NameUserIdProvider>();

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
