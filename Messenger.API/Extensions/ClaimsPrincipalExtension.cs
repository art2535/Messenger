using Messenger.Application.Interfaces;
using Messenger.Application.DTOs.Users;
using System.Security.Claims;

namespace Messenger.API.Extensions
{
    public static class ClaimsPrincipalExtension
    {
        extension(ClaimsPrincipal user)
        {
            /// <summary>
            /// Извлекает внешний идентификатор (externalId) текущего пользователя из Claims.
            /// </summary>
            public string GetExternalId()
            {
                return user.FindFirst("sub")?.Value
                    ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value
                    ?? throw new UnauthorizedAccessException("Не удалось определить пользователя");
            }

            /// <summary>
            /// Возвращает объект текущего авторизованного пользователя из базы данных.
            /// </summary>
            public async Task<UserDto> GetCurrentUserAsync(IUserService userService)
            {
                var externalId = user.GetExternalId();

                return await userService.GetUserByExternalIdAsync(externalId)
                       ?? throw new UnauthorizedAccessException("Пользователь не найден");
            }

            /// <summary>
            /// Возвращает внутренний идентификатор (UserId) текущего авторизованного пользователя.
            /// </summary>
            public async Task<Guid> GetCurrentUserIdAsync(IUserService userService)
            {
                var currentUser = await user.GetCurrentUserAsync(userService);
                return currentUser.UserId;
            }
        }
    }
}