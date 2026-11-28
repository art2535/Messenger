using Xunit;

namespace Messenger.Tests.Shared.Fixtures;

[CollectionDefinition("PostgreSQL integration")]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration";
}
