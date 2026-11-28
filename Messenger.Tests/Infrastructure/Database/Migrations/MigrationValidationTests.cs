using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

using Messenger.Tests.Shared.Fixtures;

namespace Messenger.Tests.Infrastructure.Database.Migrations;

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

