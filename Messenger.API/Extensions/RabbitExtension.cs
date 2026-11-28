using MassTransit;
using Messenger.API.Consumers;
using Messenger.Infrastructure.Data;

namespace Messenger.API.Extensions
{
    public static class RabbitExtension
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddRabbitMQ(IConfiguration configuration)
            {
                var connectionString = configuration.GetConnectionString("RabbitMQ");
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException(
                        "Не задана строка подключения RabbitMQ. Настройте ConnectionStrings:RabbitMQ через appsettings, User Secrets или ConnectionStrings__RabbitMQ.");

                if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var rabbitUri) ||
                    (rabbitUri.Scheme != "amqp" && rabbitUri.Scheme != "amqps"))
                    throw new InvalidOperationException("ConnectionStrings:RabbitMQ должен содержать абсолютный AMQP/AMQPS URI.");

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
                        cfg.Host(rabbitUri);
                        cfg.UseMessageRetry(r => r.Incremental(5, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)));
                        cfg.UseConcurrencyLimit(30);
                        cfg.PrefetchCount = 32;
                        cfg.ConfigureEndpoints(context);
                    });
                });
                return services;
            }
        }
    }
}