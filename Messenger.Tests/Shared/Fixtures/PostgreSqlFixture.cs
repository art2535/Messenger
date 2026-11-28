using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Messenger.Tests.Shared.Fixtures;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
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

