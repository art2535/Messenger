using System.Collections.Concurrent;
using Messenger.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Messenger.Infrastructure.Services;

/// <summary>
/// Распределённый учёт SignalR-подключений через Redis Sorted Set с истекающими lease.
/// </summary>
public sealed class RedisUserPresenceTracker : IUserPresenceTracker
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    private readonly IDatabase? _database;
    private readonly ILogger<RedisUserPresenceTracker> _logger;
    private readonly ConcurrentDictionary<string, Guid> _localConnections = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _localUsers = new();

    private const string ConnectScript = """
        local now = tonumber(ARGV[1])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now)
        local previous = redis.call('ZCARD', KEYS[1])
        redis.call('ZADD', KEYS[1], tonumber(ARGV[2]), ARGV[3])
        redis.call('PEXPIRE', KEYS[1], tonumber(ARGV[4]))
        return previous
        """;

    private const string DisconnectScript = """
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', tonumber(ARGV[1]))
        redis.call('ZREM', KEYS[1], ARGV[2])
        local count = redis.call('ZCARD', KEYS[1])
        if count == 0 then redis.call('DEL', KEYS[1]) end
        return count
        """;

    private const string HasConnectionsScript = """
        local now = tonumber(ARGV[1])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now)
        local count = redis.call('ZCARD', KEYS[1])
        if count == 0 then redis.call('DEL', KEYS[1]) end
        return count
        """;

    private const string HeartbeatScript = """
        local now = tonumber(ARGV[1])
        local expiry = tonumber(ARGV[2])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now)
        for i = 3, #ARGV - 1 do
            redis.call('ZADD', KEYS[1], expiry, ARGV[i])
        end
        if redis.call('ZCARD', KEYS[1]) > 0 then
            redis.call('PEXPIRE', KEYS[1], tonumber(ARGV[#ARGV]))
        end
        return redis.call('ZCARD', KEYS[1])
        """;

    public RedisUserPresenceTracker(
        IConnectionMultiplexer? redis,
        ILogger<RedisUserPresenceTracker> logger)
    {
        _database = redis?.GetDatabase();
        _logger = logger;
        if (_database is null)
            _logger.LogWarning("Redis для Presence не настроен: используется локальный режим, допустимый только для одного экземпляра API.");
    }

    public async Task<bool> ConnectAsync(Guid userId, string connectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_localConnections.TryAdd(connectionId, userId))
            return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expiry = now + (long)LeaseDuration.TotalMilliseconds;
        try
        {
            bool becameOnline;
            if (_database is null)
            {
                var connections = _localUsers.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
                lock (connections)
                {
                    becameOnline = connections.IsEmpty;
                    connections.TryAdd(connectionId, 0);
                }
            }
            else
            {
                var previous = (long)await _database.ScriptEvaluateAsync(
                    ConnectScript,
                    new RedisKey[] { GetKey(userId) },
                    new RedisValue[] { now, expiry, connectionId, (long)(LeaseDuration.TotalMilliseconds * 2) });
                becameOnline = previous == 0;
                _localUsers.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>()).TryAdd(connectionId, 0);
            }

            return becameOnline;
        }
        catch
        {
            _localConnections.TryRemove(connectionId, out _);
            throw;
        }
    }

    public async Task<bool> DisconnectAsync(Guid userId, string connectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_localConnections.TryRemove(connectionId, out var trackedUserId))
            return false;

        if (trackedUserId != userId)
        {
            _logger.LogWarning("Presence connection {ConnectionId} принадлежит {TrackedUserId}, отключается для {UserId}.",
                connectionId, trackedUserId, userId);
            userId = trackedUserId;
        }

        if (_localUsers.TryGetValue(userId, out var localConnections))
        {
            lock (localConnections)
            {
                localConnections.TryRemove(connectionId, out _);
                if (localConnections.IsEmpty)
                    _localUsers.TryRemove(userId, out _);
            }
        }

        if (_database is null)
            return !_localUsers.TryGetValue(userId, out var remaining) || remaining.IsEmpty;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var count = (long)await _database.ScriptEvaluateAsync(
            DisconnectScript,
            new RedisKey[] { GetKey(userId) },
            new RedisValue[] { now, connectionId });
        return count == 0;
    }

    /// <summary>Проверяет наличие хотя бы одного неистёкшего подключения пользователя.</summary>
    public async Task<bool> HasActiveConnectionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_database is null)
            return _localUsers.TryGetValue(userId, out var connections) && !connections.IsEmpty;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var count = (long)await _database.ScriptEvaluateAsync(
            HasConnectionsScript,
            new RedisKey[] { GetKey(userId) },
            new RedisValue[] { now });
        cancellationToken.ThrowIfCancellationRequested();
        return count > 0;
    }

    public async Task HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_database is null)
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expiry = now + (long)LeaseDuration.TotalMilliseconds;
        var ttl = (long)(LeaseDuration.TotalMilliseconds * 2);

        foreach (var user in _localUsers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var connections = user.Value.Keys.ToArray();
            if (connections.Length == 0)
                continue;

            var args = new RedisValue[connections.Length + 3];
            args[0] = now;
            args[1] = expiry;
            for (var i = 0; i < connections.Length; i++)
                args[i + 2] = connections[i];
            args[^1] = ttl;

            await _database.ScriptEvaluateAsync(HeartbeatScript, new RedisKey[] { GetKey(user.Key) }, args);
        }
    }

    private static RedisKey GetKey(Guid userId) => $"messenger:presence:{userId:N}";
}
