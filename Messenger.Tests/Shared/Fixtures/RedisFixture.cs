using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Messenger.Tests.Shared.Fixtures;

public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine")
        .Build();

    public IConnectionMultiplexer Connection { get; private set; } = null!;
    public IDatabase Database => Connection.GetDatabase();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var options = ConfigurationOptions.Parse(_container.GetConnectionString());
        options.AbortOnConnectFail = false;
        options.ConnectRetry = 10;
        options.KeepAlive = 5;
        options.ReconnectRetryPolicy = new ExponentialRetry(100);
        Connection = await ConnectionMultiplexer.ConnectAsync(options);
    }

    public Task PauseAsync() => _container.PauseAsync();

    public Task ResumeAsync() => _container.UnpauseAsync();

    public async Task DisposeAsync()
    {
        Connection.Dispose();
        await _container.DisposeAsync();
    }
}
