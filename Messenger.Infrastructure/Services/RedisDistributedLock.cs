using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Messenger.Infrastructure.Services
{
    public sealed class RedisDistributedLock
    {
        private readonly IDatabase? _db;
        private readonly ILogger<RedisDistributedLock> _logger;
        private readonly bool _enabled;

        private const string ReleaseScript = """
            if redis.call('get', KEYS[1]) == ARGV[1] then
                return redis.call('del', KEYS[1])
            else
                return 0
            end
            """;

        public RedisDistributedLock(IConnectionMultiplexer? redis, ILogger<RedisDistributedLock> logger)
        {
            _logger = logger;
            try
            {
                _db = redis?.GetDatabase();
                _enabled = _db is not null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RedisDistributedLock: Redis недоступен — lock отключён (single-instance fallback)");
                _db = null;
                _enabled = false;
            }
        }

        public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (!_enabled || _db is null)
            {
                return new NoOpLock();
            }

            var token = Guid.NewGuid().ToString("N");
            Task<bool>? acquisitionTask = null;
            try
            {
                acquisitionTask = _db.StringSetAsync(key, token, ttl, When.NotExists);
                var acquired = await acquisitionTask.WaitAsync(ct);
                if (!acquired)
                {
                    _logger.LogDebug("DistributedLock: ключ {Key} уже занят другой репликой", key);
                    return null;
                }

                _logger.LogDebug("DistributedLock: захвачен {Key} ttl={Ttl}s", key, (int)ttl.TotalSeconds);
                return new RedisLockHandle(_db, key, token, _logger);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (acquisitionTask is not null)
                    _ = ReleaseIfAcquiredAsync(acquisitionTask, key, token);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DistributedLock: ошибка захвата {Key} — пропускаем тик", key);
                return null;
            }
        }

        private async Task ReleaseIfAcquiredAsync(Task<bool> acquisitionTask, string key, string token)
        {
            try
            {
                if (!await acquisitionTask.ConfigureAwait(false))
                    return;

                await _db!.ScriptEvaluateAsync(
                    ReleaseScript,
                    new RedisKey[] { key },
                    new RedisValue[] { token }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "DistributedLock: не удалось компенсировать захват {Key} после отмены ожидания; ключ истечёт по TTL",
                    key);
            }
        }

        private sealed class NoOpLock : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class RedisLockHandle : IAsyncDisposable
        {
            private readonly IDatabase _db;
            private readonly string _key;
            private readonly string _token;
            private readonly ILogger _logger;
            private int _released;

            public RedisLockHandle(IDatabase db, string key, string token, ILogger logger)
            {
                _db = db;
                _key = key;
                _token = token;
                _logger = logger;
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0)
                    return;

                try
                {
                    await _db.ScriptEvaluateAsync(
                        ReleaseScript,
                        new RedisKey[] { _key },
                        new RedisValue[] { _token });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "DistributedLock: ошибка освобождения {Key}", _key);
                }
            }
        }
    }
}
