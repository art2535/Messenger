using System.Collections.Concurrent;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Messenger.Tests.Shared.Fixtures;
using Xunit;

namespace Messenger.Tests.Api.Messaging;

[Collection(RabbitMqCollection.Name)]
public sealed class RabbitMqTransportIntegrationTests(RabbitMqFixture fixture)
{
    [Fact]
    public async Task PublishedMessage_IsConsumedThroughRabbitMqTransport()
    {
        var probe = new RabbitMessageProbe();
        var messageId = Guid.NewGuid();
        var completion = probe.Register(messageId);
        var queueName = "messenger-test-" + Guid.NewGuid().ToString("N");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(probe);
        services.AddMassTransit(registration =>
        {
            registration.AddConsumer<RabbitProbeConsumer>();
            registration.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(fixture.ConnectionString));
                cfg.ReceiveEndpoint(queueName, endpoint =>
                    endpoint.ConfigureConsumer<RabbitProbeConsumer>(context));
            });
        });

        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            await bus.Publish(new RabbitProbeMessage(messageId, "transport-round-trip"));

            var received = await completion.WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal("transport-round-trip", received);
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    public sealed record RabbitProbeMessage(Guid Id, string Payload);

    public sealed class RabbitMessageProbe
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<string>> _pending = new();

        public Task<string> Register(Guid id)
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(id, completion))
                throw new InvalidOperationException("Probe id is already registered.");
            return completion.Task;
        }

        public void Complete(Guid id, string payload)
        {
            if (_pending.TryGetValue(id, out var completion))
                completion.TrySetResult(payload);
        }
    }

    public sealed class RabbitProbeConsumer(RabbitMessageProbe probe) : IConsumer<RabbitProbeMessage>
    {
        public Task Consume(ConsumeContext<RabbitProbeMessage> context)
        {
            probe.Complete(context.Message.Id, context.Message.Payload);
            return Task.CompletedTask;
        }
    }
}
