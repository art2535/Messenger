using MassTransit;
using Messenger.API.Consumers;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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

                    x.AddConfigureEndpointsCallback((name, cfg) =>
                    {
                        if (cfg is IRabbitMqReceiveEndpointConfigurator rmq)
                        {
                            rmq.SetQuorumQueue(1);
                        }
                    });

                    x.UsingRabbitMq((context, cfg) =>
                    {
                        var connectionString = configuration.GetConnectionString("RabbitMQ");

                        if (!string.IsNullOrWhiteSpace(connectionString))
                        {
                            cfg.Host(new Uri(connectionString));
                        }
                        else
                        {
                            var rabbitConfig = configuration.GetSection("RabbitMQ");

                            cfg.Host(rabbitConfig["Host"], ushort.Parse(rabbitConfig["Port"] ?? "5672"), "/", h =>
                            {
                                h.Username(rabbitConfig["Username"] ?? "guest");
                                h.Password(rabbitConfig["Password"] ?? "guest");
                            });
                        }

                        cfg.UseMessageRetry(r =>
                        {
                            r.Incremental(5, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
                        });

                        cfg.UseConcurrencyLimit(30);
                        cfg.PrefetchCount = 15;

                        cfg.ConfigureEndpoints(context);
                    });
                });

                return services;
            }
        }
    }
}