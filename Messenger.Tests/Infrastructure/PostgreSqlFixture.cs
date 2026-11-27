using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Messenger.Tests.Infrastructure;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public DbContextOptions<GuapMessengerContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Options = new DbContextOptionsBuilder<GuapMessengerContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .EnableDetailedErrors()
            .Options;

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public GuapMessengerContext CreateContext() => new(Options);

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}

[CollectionDefinition("PostgreSQL integration")]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration";
}
