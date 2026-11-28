using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Messenger.Tests.Shared.Fixtures;

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
