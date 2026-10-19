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
                        var connectionString =
                            configuration.GetConnectionString("RabbitMQ")
                            ?? configuration["ConnectionStrings:RabbitMQ"];

                        if (string.IsNullOrWhiteSpace(connectionString))
                        {
                            throw new InvalidOperationException(
                                "Не задана строка подключения RabbitMQ. " +
                                "Укажите ConnectionStrings:RabbitMQ " +
                                "(например amqp://guest:guest@localhost:5672) " +
                                "или передайте её через Aspire / user-secrets / env.");
                        }

                        cfg.Host(new Uri(connectionString));

                        cfg.UseMessageRetry(r =>
                        {
                            r.Incremental(5, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
                        });

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