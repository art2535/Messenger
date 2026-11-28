namespace Messenger.Tests.Shared.TestDoubles;

internal sealed class NoOpCacheService : Messenger.Application.Interfaces.ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
        Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task RemoveAsync(IEnumerable<string> keys, CancellationToken ct = default) =>
        Task.CompletedTask;
}
