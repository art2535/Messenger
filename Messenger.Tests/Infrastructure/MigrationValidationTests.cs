using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace Messenger.Tests.Infrastructure;

[Collection(PostgreSqlMigrationCollection.Name)]
public sealed class MigrationValidationTests(PostgreSqlMigrationFixture fixture)
{
    [Fact]
    public async Task AllMigrations_ApplyToAnEmptyPostgreSqlDatabase_AndMatchTheModelSnapshot()
    {
        await using var context = fixture.CreateContext();

        var expectedMigrations = context.Database.GetMigrations().ToArray();
        Assert.NotEmpty(expectedMigrations);
        Assert.False(context.Database.HasPendingModelChanges(),
            "EF model differs from GuapMessengerContextModelSnapshot. Add or correct a migration.");

        var pendingBeforeMigration = (await context.Database.GetPendingMigrationsAsync()).ToArray();
        Assert.Equal(expectedMigrations, pendingBeforeMigration);

        await context.Database.MigrateAsync();

        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(expectedMigrations, appliedMigrations);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        if (context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            await context.Database.OpenConnectionAsync();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT
                to_regclass('"Users"') IS NOT NULL
                AND to_regclass('"Messages"') IS NOT NULL
                AND to_regclass('"Scheduled_Messages"') IS NOT NULL
                AND to_regclass('"OutboxMessage"') IS NOT NULL
                AND to_regclass('logging."Logs"') IS NOT NULL;
            """;

        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public void IdempotentMigrationScript_ContainsTheFullMigrationChain()
    {
        using var context = fixture.CreateContext();
        var migrator = context.GetService<IMigrator>();
        var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.False(string.IsNullOrWhiteSpace(script));
        Assert.Contains("__EFMigrationsHistory", script);
        Assert.Contains("CREATE TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("logging", script, StringComparison.OrdinalIgnoreCase);

        foreach (var migration in context.Database.GetMigrations())
            Assert.Contains(migration, script);
    }
}

public sealed class PostgreSqlMigrationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    private DbContextOptions<GuapMessengerContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<GuapMessengerContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .EnableDetailedErrors()
            .Options;
    }

    public GuapMessengerContext CreateContext() => new(_options);

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}

[CollectionDefinition("PostgreSQL migration validation", DisableParallelization = true)]
public sealed class PostgreSqlMigrationCollection : ICollectionFixture<PostgreSqlMigrationFixture>
{
    public const string Name = "PostgreSQL migration validation";
}
