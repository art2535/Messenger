using Messenger.API.Options;
using Messenger.Core.Hubs;
using Messenger.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Messenger.API.Services
{
    public class SessionCleanupBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ILogger<SessionCleanupBackgroundService> _logger;
        private readonly SessionCleanupOptions _options;

        public SessionCleanupBackgroundService(IServiceScopeFactory scopeFactory, IHubContext<ChatHub> hubContext,
            IOptions<SessionCleanupOptions> options, ILogger<SessionCleanupBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _hubContext = hubContext;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
            var idleThreshold = TimeSpan.FromMinutes(Math.Max(5, _options.IdleMinutes));

            _logger.LogInformation(
                "SessionCleanupBackgroundService запущен. Интервал: {Interval}, порог простоя: {Idle}",
                interval, idleThreshold);

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CleanupOnceAsync(idleThreshold, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при очистке неактивных сессий");
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("SessionCleanupBackgroundService остановлен");
        }

        private async Task CleanupOnceAsync(TimeSpan idleThreshold, CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var statusService = scope.ServiceProvider.GetRequiredService<IUserStatusService>();
            var loginService = scope.ServiceProvider.GetRequiredService<ILoginService>();

            var olderThan = DateTime.Now - idleThreshold;

            var inactive = await statusService.GetInactiveOnlineStatusesAsync(olderThan, ct);
            if (inactive.Count == 0)
            {
                _logger.LogDebug("Неактивных онлайн-пользователей не найдено");
                return;
            }

            var userIds = inactive.Select(s => s.UserId).Distinct().ToList();

            _logger.LogInformation(
                "Очистка: {Count} пользователей неактивны дольше {Idle}. UserIds: {Ids}",
                userIds.Count, idleThreshold, string.Join(", ", userIds));

            await statusService.SetOfflineBatchAsync(userIds, ct);

            await loginService.CloseActiveLoginsForUsersAsync(userIds, ct);

            var now = DateTime.Now;
            foreach (var userId in userIds)
            {
                var payload = new
                {
                    userId = userId.ToString(),
                    isOnline = false,
                    lastActivity = now
                };

                try
                {
                    await _hubContext.Clients.All.SendAsync("UserOnlineStatusChanged", payload, ct);
                    await _hubContext.Clients.User(userId.ToString())
                        .SendAsync("UserOnlineStatusChanged", payload, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Не удалось отправить SignalR UserOnlineStatusChanged для {UserId}", userId);
                }
            }

            _logger.LogInformation("Очистка завершена: offline + закрыты сессии для {Count} пользователей", userIds.Count);
        }
    }
}
