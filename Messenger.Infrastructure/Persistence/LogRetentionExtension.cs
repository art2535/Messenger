using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Infrastructure.Persistence
{
    public static class LogRetentionExtension
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddLogRetention(IConfiguration configuration)
            {
                services.Configure<LogRetentionOptions>(configuration.GetSection(LogRetentionOptions.SectionName));
                services.AddHostedService<LogRetentionService>();
                return services;
            }
        }
    }
}
