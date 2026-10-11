using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Messenger.Infrastructure.Services
{
    public sealed class RedisDistributedLock
    {
        private readonly IDatabase? _db;
        private readonly ILogger<RedisDistributedLock> _logger;
        private readonly bool _enabled;

        public RedisDistributedLock(IConnectionMultiplexer? redis, ILogger<RedisDistributedLock> logger)
        {
            _logger = logger;
            try
            {
                _db = redis?.GetDatabase();
                _enabled = _db is not null;
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
            if (!_enabled || _db is null)
            {
                return new NoOpLock();
            }

            var token = Guid.NewGuid().ToString("N");
            try
            {
                var acquired = await _db.StringSetAsync(key, token, ttl, When.NotExists);
                if (!acquired)
                {
                    _logger.LogDebug("DistributedLock: ключ {Key} уже занят другой репликой", key);
                    return null;
                }

                _logger.LogDebug("DistributedLock: захвачен {Key} ttl={Ttl}s", key, (int)ttl.TotalSeconds);
                return new RedisLockHandle(_db, key, token, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DistributedLock: ошибка захвата {Key} — пропускаем тик", key);
                return null;
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
                    const string script = """
                        if redis.call('get', KEYS[1]) == ARGV[1] then
                            return redis.call('del', KEYS[1])
                        else
                            return 0
                        end
                        """;
                    await _db.ScriptEvaluateAsync(script, new RedisKey[] { _key }, new RedisValue[] { _token });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "DistributedLock: ошибка освобождения {Key}", _key);
                }
            }
        }
    }
}
