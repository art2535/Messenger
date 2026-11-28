using Messenger.API.Options;
using Messenger.API.Providers;
using Messenger.API.Services;
using Messenger.Application.Interfaces;
using Messenger.Application.Services;
using Messenger.Infrastructure.Services;
using Messenger.Core.Configuration;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;
using WebPush;

namespace Messenger.API.Extensions
{
    public static class ServiceExtension
    {
        private static string? GetRedisConnectionString(IConfiguration configuration) =>
            configuration.GetConnectionString("Redis");

        extension(IServiceCollection services)
        {
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
                services.AddSingleton<IUserPresenceTracker>(sp => new RedisUserPresenceTracker(
                    sp.GetService<IConnectionMultiplexer>(),
                    sp.GetRequiredService<ILogger<RedisUserPresenceTracker>>()));
                services.AddScoped<IBroadcastService, BroadcastService>();
                services.AddScoped<IBroadcastRepository, EfBroadcastRepository>();
                services.AddScoped<IMessageService, MessageService>();
                services.AddScoped<IAdminService, AdminService>();
                services.AddScoped<IModerationService, ModerationService>();
                services.AddSingleton<IRateLimitService>(sp => new RedisRateLimitService(
                    sp.GetService<IConnectionMultiplexer>(),
                    sp.GetRequiredService<ILogger<RedisRateLimitService>>()));
                services.AddSingleton<WebPushClient>();
                services.AddScoped<IPushSubscriptionService, PushSubscriptionService>();
                services.AddScoped<IScheduledMessageService, ScheduledMessageService>();
                return services;
            }

            public IServiceCollection AddRedisCache(IConfiguration configuration)
            {
                var redisCs = GetRedisConnectionString(configuration);
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
                        new RedisCacheService(sp.GetRequiredService<IConnectionMultiplexer>(),
                            sp.GetRequiredService<ILogger<RedisCacheService>>()));
                    services.AddSingleton<RedisDistributedLock>(sp =>
                        new RedisDistributedLock(sp.GetRequiredService<IConnectionMultiplexer>(),
                            sp.GetRequiredService<ILogger<RedisDistributedLock>>()));
                }
                else
                {
                    services.AddSingleton<ICacheService>(sp =>
                        new RedisCacheService(null, sp.GetRequiredService<ILogger<RedisCacheService>>()));
                    services.AddSingleton<RedisDistributedLock>(sp =>
                        new RedisDistributedLock(null, sp.GetRequiredService<ILogger<RedisDistributedLock>>()));
                }
                return services;
            }

            public IServiceCollection AddSessionCleanup(IConfiguration configuration)
            {
                services.AddOptions<SessionCleanupOptions>()
                    .Bind(configuration.GetSection(SessionCleanupOptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
                services.AddHostedService<SessionCleanupBackgroundService>();
                services.AddHostedService<PresenceHeartbeatBackgroundService>();
                return services;
            }

            public IServiceCollection AddSignalRService(IConfiguration configuration)
            {
                var signalROptions = configuration.GetSection(SignalROptions.SectionName)
                    .Get<SignalROptions>() ?? new SignalROptions();

                services.AddOptions<SignalROptions>()
                    .Bind(configuration.GetSection(SignalROptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                var redisCs = GetRedisConnectionString(configuration);
                var signalR = services.AddSignalR(options =>
                {
                    options.MaximumReceiveMessageSize = signalROptions.MaximumReceiveMessageSize;
                    options.EnableDetailedErrors = signalROptions.EnableDetailedErrors;
                    options.ClientTimeoutInterval = TimeSpan.FromSeconds(signalROptions.ClientTimeoutSeconds);
                    options.KeepAliveInterval = TimeSpan.FromSeconds(signalROptions.KeepAliveSeconds);
                });

                if (!string.IsNullOrWhiteSpace(redisCs))
                    signalR.AddStackExchangeRedis(redisCs, options =>
                        options.Configuration.ChannelPrefix = RedisChannel.Literal(signalROptions.ChannelPrefix));

                services.AddSingleton<IUserIdProvider, NameUserIdProvider>();
                return services;
            }

            public IServiceCollection AddEncryption(IConfiguration configuration)
            {
                services.AddOptions<AesGcmEncryptionOptions>()
                    .Bind(configuration.GetSection("Encryption"))
                    .Validate(options =>
                    {
                        if (string.IsNullOrWhiteSpace(options.MasterKeyBase64))
                            return false;
                        try { return Convert.FromBase64String(options.MasterKeyBase64.Trim()).Length == 32; }
                        catch (FormatException) { return false; }
                    }, "Encryption:MasterKeyBase64 должен быть валидным Base64-ключом длиной ровно 32 байта.")
                    .ValidateOnStart();

                services.AddSingleton<IEncryptionService, AesGcmEncryptionService>();
                return services;
            }
        }
    }
}