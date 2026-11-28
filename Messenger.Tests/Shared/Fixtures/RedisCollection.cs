using Xunit;

namespace Messenger.Tests.Shared.Fixtures;

[CollectionDefinition("Redis integration")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "Redis integration";
}
