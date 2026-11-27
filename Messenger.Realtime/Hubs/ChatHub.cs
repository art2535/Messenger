using Messenger.Application.Interfaces;
using Messenger.Realtime.Metrics;
using Messenger.Realtime.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Messenger.Realtime.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IUserService _userService;
        private readonly IUserStatusService _userStatusService;
        private readonly IUserPresenceTracker _presenceTracker;
        private readonly IChatService _chatService;
        private readonly IAccountSettingsService _accountSettingsService;
        private readonly ILogger<ChatHub> _logger;
        private readonly TypingRateLimiterService _typingLimiter;
        private readonly SendMessageRateLimiterService _sendMessageLimiter;

        public ChatHub(IUserService userService, IUserStatusService userStatusService, IUserPresenceTracker presenceTracker, IChatService chatService,
            IAccountSettingsService accountSettingsService,
            ILogger<ChatHub> logger, TypingRateLimiterService typingLimiter, SendMessageRateLimiterService sendMessageLimiter)
        {
            _userService = userService;
            _userStatusService = userStatusService;
            _presenceTracker = presenceTracker;
            _chatService = chatService;
            _accountSettingsService = accountSettingsService;
            _logger = logger;
            _typingLimiter = typingLimiter;
            _sendMessageLimiter = sendMessageLimiter;
        }

        private async Task SendToPeersAsync(Guid userId, string method, object payload, CancellationToken ct = default)
        {
            var peerIds = await _chatService.GetPeerUserIdsAsync(userId, ct);
            var groups = peerIds.Select(id => $"User_{id}").ToList();
            if (groups.Count == 0)
            {
                await Clients.Group($"User_{userId}").SendAsync(method, payload, ct);
                return;
            }
            await Clients.Groups(groups).SendAsync(method, payload, ct);
        }

        private async Task<Guid> ResolveCurrentUserIdAsync(CancellationToken cancellationToken)
        {
            var externalId = Context.User?.FindFirst("sub")?.Value
                ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(externalId))
                throw new HubException("Не удалось определить пользователя.");

            var user = await _userService.GetUserByExternalIdAsync(externalId, cancellationToken);
            return user?.UserId ?? throw new HubException("Пользователь не найден.");
        }

        private async Task EnsureChatMembershipAsync(Guid chatId, Guid userId, CancellationToken cancellationToken)
        {
            // Вариант метода с actorId проверяет членство до возврата списка участников.
            await _chatService.GetChatParticipantsAsync(chatId, userId, cancellationToken);
        }

        public async Task JoinChat(Guid chatId)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var userId = await ResolveCurrentUserIdAsync(Context.ConnectionAborted);
                await EnsureChatMembershipAsync(chatId, userId, Context.ConnectionAborted);
                await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString(), Context.ConnectionAborted);

                // Закрывает гонку между первичной проверкой и добавлением в группу.
                try
                {
                    await EnsureChatMembershipAsync(chatId, userId, Context.ConnectionAborted);
                }
                catch
                {
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString(), CancellationToken.None);
                    throw;
                }
            }
            finally
            {
                var duration = (DateTime.UtcNow - startTime).TotalSeconds;
                MessengerMetrics.HubActionDuration.Record(duration,
                    new KeyValuePair<string, object?>("action", "join_chat"));
            }
        }

        public async Task LeaveChat(Guid chatId)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString(), Context.ConnectionAborted);
            }
            finally
            {
                var duration = (DateTime.UtcNow - startTime).TotalSeconds;
                MessengerMetrics.HubActionDuration.Record(duration,
                    new KeyValuePair<string, object?>("action", "leave_chat"));
            }
        }

        public override async Task OnConnectedAsync()
        {
            try
            {
                var externalId = Context.User?.FindFirst("sub")?.Value
                              ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(externalId))
                {
                    _logger.LogWarning("OnConnectedAsync: ExternalId не найден");
                    await base.OnConnectedAsync();
                    return;
                }

                var user = await _userService.GetUserByExternalIdAsync(externalId, Context.ConnectionAborted);
                if (user == null)
                {
                    _logger.LogWarning("OnConnectedAsync: Пользователь с ExternalId {ExternalId} не найден", externalId);
                    await base.OnConnectedAsync();
                    return;
                }

                var userId = user.UserId;
                Context.Items["PresenceUserId"] = userId;

                _logger.LogInformation("OnConnectedAsync: {UserId} подключился", userId);

                MessengerMetrics.SignalRActiveConnections.Add(1, 
                    new KeyValuePair<string, object?>("hub", "chat"));

                await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}", Context.ConnectionAborted);

                var becameOnline = await _presenceTracker.ConnectAsync(userId, Context.ConnectionId, Context.ConnectionAborted);
                if (!becameOnline)
                {
                    await base.OnConnectedAsync();
                    return;
                }

                await _userStatusService.UpdateUserStatusAsync(userId, true, Context.ConnectionAborted);

                var showOnline = true;
                try
                {
                    var settings = await _accountSettingsService.GetByUserIdAsync(userId, Context.ConnectionAborted);
                    showOnline = settings.ShowOnline;
                }
                catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось загрузить настройки для {UserId}, online по умолчанию", userId);
                }

                if (showOnline)
                {
                    var statusData = new
                    {
                        userId = userId.ToString(),
                        isOnline = true,
                        lastActivity = DateTime.UtcNow
                    };
                    await SendToPeersAsync(userId, "UserOnlineStatusChanged", statusData, Context.ConnectionAborted);
                }
            }
            catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в OnConnectedAsync");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                Guid? userId = Context.Items.TryGetValue("PresenceUserId", out var presenceUserId)
                    && presenceUserId is Guid trackedUserId
                    ? trackedUserId
                    : null;

                if (!userId.HasValue)
                {
                    var externalId = Context.User?.FindFirst("sub")?.Value
                                  ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (!string.IsNullOrEmpty(externalId))
                    {
                        var user = await _userService.GetUserByExternalIdAsync(externalId);
                        userId = user?.UserId;
                    }
                }

                if (userId.HasValue)
                {
                    MessengerMetrics.SignalRActiveConnections.Add(-1,
                        new KeyValuePair<string, object?>("hub", "chat"));
                    var becameOffline = await _presenceTracker.DisconnectAsync(userId.Value, Context.ConnectionId);
                    if (becameOffline)
                    {
                        await _userStatusService.UpdateUserStatusAsync(userId.Value, false);

                        var statusData = new
                        {
                            userId = userId.Value.ToString(),
                            isOnline = false,
                            lastActivity = DateTime.UtcNow
                        };
                        await SendToPeersAsync(userId.Value, "UserOnlineStatusChanged", statusData);
                    }
                }

                if (exception != null)
                {
                    MessengerMetrics.SignalRErrors.Add(1, 
                        new KeyValuePair<string, object?>("hub", "chat"),
                        new KeyValuePair<string, object?>("error_type", exception.GetType().Name));
                }
            }
            catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
            {
                _logger.LogDebug("Операция OnDisconnectedAsync отменена закрытием соединения");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в OnDisconnectedAsync");
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task<bool> TryAcquireSendMessagePermit()
        {
            var externalId = Context.User?.FindFirst("sub")?.Value
                ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(externalId))
                return false;

            return await _sendMessageLimiter.TryAcquireAsync(externalId, Context.ConnectionAborted);
        }

        public async Task StartTyping(string chatId)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                if (!Guid.TryParse(chatId, out var parsedChatId))
                    return;

                var externalId = Context.User?.FindFirst("sub")?.Value
                    ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(externalId))
                    return;

                if (!await _typingLimiter.TryAcquireAsync(externalId, Context.ConnectionAborted))
                    return;

                var user = await _userService.GetUserByExternalIdAsync(externalId, Context.ConnectionAborted);
                if (user == null)
                    return;

                await EnsureChatMembershipAsync(parsedChatId, user.UserId, Context.ConnectionAborted);

                try
                {
                    var settings = await _accountSettingsService.GetByUserIdAsync(user.UserId, Context.ConnectionAborted);
                    if (!settings.ShowTyping)
                        return;
                }
                catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
                {
                    throw;
                }
                catch { }

                await Clients.Group(parsedChatId.ToString()).SendAsync("UserIsTyping", new
                {
                    chatId,
                    userId = user.UserId.ToString(),
                    isTyping = true
                }, Context.ConnectionAborted);
            }
            finally
            {
                var duration = (DateTime.UtcNow - startTime).TotalSeconds;
                MessengerMetrics.HubActionDuration.Record(duration,
                    new KeyValuePair<string, object?>("action", "start_typing"));
            }
        }

        public async Task StopTyping(string chatId)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                if (!Guid.TryParse(chatId, out var parsedChatId))
                    return;

                var externalId = Context.User?.FindFirst("sub")?.Value
                    ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(externalId))
                    return;

                if (!await _typingLimiter.TryAcquireAsync(externalId, Context.ConnectionAborted))
                    return;

                var user = await _userService.GetUserByExternalIdAsync(externalId, Context.ConnectionAborted);
                if (user == null)
                    return;

                await EnsureChatMembershipAsync(parsedChatId, user.UserId, Context.ConnectionAborted);

                try
                {
                    var settings = await _accountSettingsService.GetByUserIdAsync(user.UserId, Context.ConnectionAborted);
                    if (!settings.ShowTyping)
                        return;
                }
                catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
                {
                    throw;
                }
                catch { }

                await Clients.Group(parsedChatId.ToString()).SendAsync("UserIsTyping", new
                {
                    chatId,
                    userId = user.UserId.ToString(),
                    isTyping = false
                }, Context.ConnectionAborted);
            }
            finally
            {
                var duration = (DateTime.UtcNow - startTime).TotalSeconds;
                MessengerMetrics.HubActionDuration.Record(duration,
                    new KeyValuePair<string, object?>("action", "stop_typing"));
            }
        }

        public async Task RequestAndBroadcastUserStatus(Guid userId)
        {
            try
            {
                var requesterId = await ResolveCurrentUserIdAsync(Context.ConnectionAborted);
                var peerIds = await _chatService.GetPeerUserIdsAsync(requesterId, Context.ConnectionAborted);
                if (requesterId != userId && !peerIds.Contains(userId))
                    return;

                var status = await _userStatusService.GetUserStatusByUserIdAsync(userId, Context.ConnectionAborted);
                if (status == null) 
                    return;

                var showOnline = true;
                try
                {
                    var settings = await _accountSettingsService.GetByUserIdAsync(userId, Context.ConnectionAborted);
                    showOnline = settings.ShowOnline;
                }
                catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
                {
                    throw;
                }
                catch { }

                var statusData = new
                {
                    userId = userId.ToString(),
                    isOnline = showOnline && status.Online,
                    lastActivity = status.LastActivity ?? DateTime.UtcNow
                };

                await SendToPeersAsync(userId, "UserOnlineStatusChanged", statusData, Context.ConnectionAborted);

                _logger.LogInformation("RequestAndBroadcastUserStatus: отправлен статус для {UserId}", userId);
            }
            catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в RequestAndBroadcastUserStatus для {UserId}", userId);
            }
        }
    }
}
