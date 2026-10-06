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
        }
    }
}
