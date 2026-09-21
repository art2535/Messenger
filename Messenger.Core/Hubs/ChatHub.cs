using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Messenger.Core.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly IUserService _userService;
        private readonly IUserStatusService _userStatusService;
        private readonly ILogger<ChatHub> _logger;
        private readonly TypingRateLimiterService _typingLimiter;

        public ChatHub(IUserService userService, IUserStatusService userStatusService, ILogger<ChatHub> logger,
            TypingRateLimiterService typingLimiter)
        {
            _userService = userService;
            _userStatusService = userStatusService;
            _logger = logger;
            _typingLimiter = typingLimiter;
        }

        public async Task JoinChat(Guid chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
        }

        public async Task LeaveChat(Guid chatId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString());
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

                var user = await _userService.GetUserByExternalIdAsync(externalId);
                if (user == null)
                {
                    _logger.LogWarning("OnConnectedAsync: Пользователь с ExternalId {ExternalId} не найден", externalId);
                    await base.OnConnectedAsync();
                    return;
                }

                var userId = user.UserId;

                _logger.LogInformation("OnConnectedAsync: {UserId} подключился", userId);

                await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");

                await _userStatusService.UpdateUserStatusAsync(new UserStatus
                {
                    UserId = userId,
                    Online = true,
                    LastActivity = DateTime.UtcNow
                });

                var statusData = new
                {
                    userId = userId.ToString(),
                    isOnline = true,
                    lastActivity = DateTime.UtcNow
                };

                await Clients.All.SendAsync("UserOnlineStatusChanged", statusData);
                await Clients.User(userId.ToString()).SendAsync("UserOnlineStatusChanged", statusData);
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
                var externalId = Context.User?.FindFirst("sub")?.Value
                              ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (!string.IsNullOrEmpty(externalId))
                {
                    var user = await _userService.GetUserByExternalIdAsync(externalId);
                    if (user != null)
                    {
                        var userId = user.UserId;

                        await _userStatusService.UpdateUserStatusAsync(new UserStatus
                        {
                            UserId = userId,
                            Online = false,
                            LastActivity = DateTime.UtcNow
                        });

                        var statusData = new
                        {
                            userId = userId.ToString(),
                            isOnline = false,
                            lastActivity = DateTime.UtcNow
                        };

                        await Clients.All.SendAsync("UserOnlineStatusChanged", statusData);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в OnDisconnectedAsync");
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task NotifyBlockStatus(string actorId, string targetId, bool isBlocked)
        {
            await Clients.Group($"User_{actorId.ToLowerInvariant()}")
                .SendAsync("UserBlockStatusChanged", new { actorId, targetId, isBlocked });
            await Clients.Group($"User_{targetId.ToLowerInvariant()}")
                .SendAsync("UserBlockStatusChanged", new { actorId, targetId, isBlocked });
        }

        public async Task NotifyParticipantRemoved(Guid chatId, Guid removedUserId)
        {
            await Clients.Group(chatId.ToString()).SendAsync("ParticipantRemoved", new
            {
                chatId = chatId,
                userId = removedUserId
            });

            await Clients.User(removedUserId.ToString()).SendAsync("YouWereRemovedFromChat", chatId);
        }

        public async Task NotifyParticipantAdded(Guid chatId, Guid addedUserId, object userInfo)
        {
            await Clients.Group(chatId.ToString()).SendAsync("ParticipantAdded", new
            {
                chatId,
                user = userInfo
            });
        }

        public async Task NotifyParticipantCountChanged(Guid chatId, int count)
        {
            await Clients.Group(chatId.ToString()).SendAsync("ParticipantCountChanged", new
            {
                chatId = chatId,
                count = count
            });
        }

        public async Task NewChat(object chatInfo)
        {
            await Task.CompletedTask;
        }

        public async Task NotifyChatDeleted(Guid chatId)
        {
            await Clients.Group(chatId.ToString()).SendAsync("ChatDeleted", chatId);
        }

        public async Task NotifyChatUpdated(Guid chatId, string newName)
        {
            await Clients.Group(chatId.ToString()).SendAsync("ChatUpdated", new
            {
                chatId,
                name = newName
            });
        }

        public async Task NotifyAvatarUpdated(string userId, string newAvatarUrl)
        {
            await Clients.All.SendAsync("AvatarUpdated", new
            {
                userId,
                avatarUrl = newAvatarUrl
            });
        }

        public async Task NotifyProfileNameUpdated(string userId, string newDisplayName)
        {
            await Clients.All.SendAsync("ProfileNameUpdated", new
            {
                userId,
                displayName = newDisplayName
            });
        }

        public async Task NotifyProfileUpdated(string userId, string? newAvatarUrl, string newDisplayName)
        {
            await Clients.All.SendAsync("ProfileUpdated", new
            {
                userId,
                avatarUrl = newAvatarUrl,
                displayName = newDisplayName
            });
        }

        public async Task StartTyping(string chatId)
        {
            var externalId = Context.User?.FindFirst("sub")?.Value 
                ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(externalId))
                return;

            if (!await _typingLimiter.TryAcquireAsync(externalId))
            {
                return;
            }

            var user = await _userService.GetUserByExternalIdAsync(externalId);
            if (user == null)
                return;

            await Clients.Group(chatId).SendAsync("UserIsTyping", new
            {
                chatId,
                userId = user.UserId.ToString(),
                isTyping = true
            });
        }

        public async Task StopTyping(string chatId)
        {
            var externalId = Context.User?.FindFirst("sub")?.Value 
                ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(externalId)) 
                return;

            if (!await _typingLimiter.TryAcquireAsync(externalId))
            {
                return;
            }

            var user = await _userService.GetUserByExternalIdAsync(externalId);
            if (user == null) 
                return;

            await Clients.Group(chatId).SendAsync("UserIsTyping", new
            {
                chatId,
                userId = user.UserId.ToString(),
                isTyping = false
            });
        }

        public async Task RequestAndBroadcastUserStatus(Guid userId)
        {
            try
            {
                var status = await _userStatusService.GetUserStatusByUserIdAsync(userId);
                if (status == null) 
                    return;

                var statusData = new
                {
                    userId = userId.ToString(),
                    isOnline = status.Online,
                    lastActivity = status.LastActivity ?? DateTime.UtcNow
                };

                await Clients.All.SendAsync("UserOnlineStatusChanged", statusData);
                await Clients.User(userId.ToString()).SendAsync("UserOnlineStatusChanged", statusData);

                _logger.LogInformation("RequestAndBroadcastUserStatus: отправлен статус для {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в RequestAndBroadcastUserStatus для {UserId}", userId);
            }
        }
    }
}
