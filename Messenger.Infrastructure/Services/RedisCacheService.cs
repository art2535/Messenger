using System.Text.Json;
using System.Text.Json.Serialization;
using Messenger.Core.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Messenger.Infrastructure.Services
{
    public class RedisCacheService : ICacheService
    {
        private readonly IDatabase? _db;
        private readonly ILogger<RedisCacheService> _logger;
        private readonly bool _enabled;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };

        public RedisCacheService(IConnectionMultiplexer? redis, ILogger<RedisCacheService> logger)
        {
            _logger = logger;
            try
            {
                _db = redis?.GetDatabase();
                _enabled = _db is not null;
                if (_enabled)
                    _logger.LogInformation("Redis cache ENABLED");
                else
                    _logger.LogWarning("Redis cache DISABLED — все запросы идут в PostgreSQL");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis недоступен — кэш отключён, работаем только через PostgreSQL");
                _db = null;
                _enabled = false;
            }
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        {
            if (!_enabled || _db is null)
            {
                _logger.LogDebug("CACHE SKIP (Redis off) key={Key} → PostgreSQL", key);
                return default;
            }

            try
            {
                var value = await _db.StringGetAsync(key);
                if (value.IsNullOrEmpty)
                {
                    _logger.LogInformation("CACHE MISS key={Key} → запрос уйдёт в PostgreSQL", key);
                    return default;
                }

                var result = JsonSerializer.Deserialize<T>((string)value!, JsonOptions);
                _logger.LogInformation("CACHE HIT key={Key} (данные из Redis, PostgreSQL не трогаем)", key);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CACHE ERROR key={Key} → fallback PostgreSQL", key);
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        {
            if (!_enabled || _db is null)
            {
                _logger.LogDebug("CACHE SET SKIP (Redis off) key={Key}", key);
                return;
            }

            try
            {
                var json = JsonSerializer.Serialize(value, JsonOptions);
                await _db.StringSetAsync(key, json, ttl);
                _logger.LogInformation("CACHE SET key={Key} ttl={TtlSeconds}s", key, (int)ttl.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CACHE SET ERROR key={Key}", key);
            }
        }

        public async Task RemoveAsync(string key, CancellationToken ct = default)
        {
            if (!_enabled || _db is null) return;

            try
            {
                var deleted = await _db.KeyDeleteAsync(key);
                _logger.LogInformation("CACHE INVALIDATE key={Key} deleted={Deleted}", key, deleted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CACHE INVALIDATE ERROR key={Key}", key);
            }
        }

        public async Task RemoveAsync(IEnumerable<string> keys, CancellationToken ct = default)
        {
            if (!_enabled || _db is null) return;

            var arr = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().Select(k => (RedisKey)k).ToArray();
            if (arr.Length == 0) return;

            try
            {
                var deleted = await _db.KeyDeleteAsync(arr);
                _logger.LogInformation("CACHE INVALIDATE keys={Count} deleted={Deleted} sample={Sample}",
                    arr.Length, deleted, arr.Length > 0 ? (string)arr[0]! : "");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CACHE INVALIDATE ERROR count={Count}", arr.Length);
            }
        }
    }
}
