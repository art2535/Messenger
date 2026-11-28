using Xunit;

namespace Messenger.Tests.Shared.Fixtures;

[CollectionDefinition("RabbitMQ integration")]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqFixture>
{
    public const string Name = "RabbitMQ integration";
}
