using Messenger.Core.Configuration;
using Messenger.Web.Providers;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace Messenger.Web.Extensions
{
    public static class SignalRExtension
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddSignalRService(IConfiguration configuration)
            {
                var signalROptions = configuration.GetSection(SignalROptions.SectionName)
                    .Get<SignalROptions>() ?? new SignalROptions();

                services.AddOptions<SignalROptions>()
                    .Bind(configuration.GetSection(SignalROptions.SectionName))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();

                var signalR = services.AddSignalR(options =>
                {
                    options.MaximumReceiveMessageSize = signalROptions.MaximumReceiveMessageSize;
                    options.EnableDetailedErrors = signalROptions.EnableDetailedErrors;
                    options.ClientTimeoutInterval = TimeSpan.FromSeconds(signalROptions.ClientTimeoutSeconds);
                    options.KeepAliveInterval = TimeSpan.FromSeconds(signalROptions.KeepAliveSeconds);
                });

                var redisCs = configuration.GetConnectionString("Redis");
                if (!string.IsNullOrWhiteSpace(redisCs))
                {
                    signalR.AddStackExchangeRedis(redisCs, options =>
                    {
                        options.Configuration.ChannelPrefix = RedisChannel.Literal(signalROptions.ChannelPrefix);
                    });
                }

                services.AddSingleton<IUserIdProvider, NameUserIdProvider>();
                return services;
            }
        }
    }
}