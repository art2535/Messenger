using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Messenger.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Messenger.Infrastructure.Services;

/// <summary>
/// Atomically counts requests in Redis. Uses a process-local fallback only when Redis
/// is not configured; Redis errors fail closed to avoid silently bypassing limits.
/// </summary>
public sealed class RedisRateLimitService : IRateLimitService
{
    private const string IncrementScript = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return current
        """;

    private readonly IDatabase? _database;
    private readonly ILogger<RedisRateLimitService> _logger;
    private readonly ConcurrentDictionary<string, LocalWindow> _localWindows = new();
    private long _operations;

    public RedisRateLimitService(
        IConnectionMultiplexer? redis,
        ILogger<RedisRateLimitService> logger)
    {
        _logger = logger;
        _database = redis?.GetDatabase();
    }

    public async ValueTask<bool> TryAcquireAsync(
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permitLimit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var key = CreateKey(partitionKey);
        var windowMilliseconds = Math.Max(1L, (long)Math.Ceiling(window.TotalMilliseconds));

        if (_database is null)
            return TryAcquireLocally(key, permitLimit, windowMilliseconds);

        try
        {
            var result = await _database.ScriptEvaluateAsync(
                IncrementScript,
                new RedisKey[] { key },
                new RedisValue[] { windowMilliseconds }).WaitAsync(cancellationToken);

            return (long)result <= permitLimit;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Redis rate limiter недоступен; запрос отклонён (fail-closed). Partition: {Partition}",
                partitionKey);
            return false;
        }
    }

    private bool TryAcquireLocally(string key, int permitLimit, long windowMilliseconds)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var counter = _localWindows.GetOrAdd(key, static _ => new LocalWindow());

        lock (counter.Sync)
        {
            if (now - counter.WindowStartedAt >= windowMilliseconds || now < counter.WindowStartedAt)
            {
                counter.WindowStartedAt = now;
                counter.Count = 0;
            }

            counter.Count++;
            var acquired = counter.Count <= permitLimit;

            if ((Interlocked.Increment(ref _operations) & 1023) == 0)
            {
                foreach (var item in _localWindows)
                {
                    lock (item.Value.Sync)
                    {
                        if (now - item.Value.WindowStartedAt >= windowMilliseconds * 2)
                            _localWindows.TryRemove(item.Key, out _);
                    }
                }
            }

            return acquired;
        }
    }

    private static string CreateKey(string partitionKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(partitionKey)));
        return "messenger:ratelimit:" + hash;
    }

    private sealed class LocalWindow
    {
        public object Sync { get; } = new();
        public long WindowStartedAt { get; set; }
        public int Count { get; set; }
    }
}
