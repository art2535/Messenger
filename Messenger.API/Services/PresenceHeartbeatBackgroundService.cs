using Messenger.Application.Interfaces;

namespace Messenger.API.Services;

/// <summary>Продлевает Redis lease активных SignalR-подключений этой реплики.</summary>
public sealed class PresenceHeartbeatBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);
    private readonly IUserPresenceTracker _tracker;
    private readonly ILogger<PresenceHeartbeatBackgroundService> _logger;

    public PresenceHeartbeatBackgroundService(IUserPresenceTracker tracker, ILogger<PresenceHeartbeatBackgroundService> logger)
    {
        _tracker = tracker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await _tracker.HeartbeatAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось продлить lease SignalR Presence.");
            }
        }
    }
}
