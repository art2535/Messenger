using Xunit;

namespace Messenger.Tests.Shared.Fixtures;

[CollectionDefinition("PostgreSQL migration validation", DisableParallelization = true)]
public sealed class PostgreSqlMigrationCollection : ICollectionFixture<PostgreSqlMigrationFixture>
{
    public const string Name = "PostgreSQL migration validation";
}
