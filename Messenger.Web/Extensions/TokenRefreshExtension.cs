using Messenger.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Messenger.Web.Extensions
{
    public static class TokenRefreshExtensions
    {
        // Ключ записи в хранилище хранится в самом cookie (AuthenticationProperties.Items).
        private const string StoreKeyItem = ".Messenger.TokenStoreKey";

        /// <summary>
        /// Подключает фоновое обновление токенов вместо TokenRefreshMiddleware.
        /// Вызывать в Program.cs: builder.Services.AddBackgroundTokenRefresh(builder.Configuration);
        /// </summary>
        public static IServiceCollection AddBackgroundTokenRefresh(
            this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<TokenRefreshOptions>(configuration.GetSection("TokenRefresh"));
            services.AddHttpClient(); // IHttpClientFactory для запросов к SSO
            services.AddSingleton<UserTokenStore>();
            services.AddSingleton<TokenRefresher>();
            services.AddHostedService<TokenRefreshBackgroundService>();

            // Фоновый сервис не видит cookie пользователя, поэтому свежие токены
            // из хранилища переносим в cookie при проверке принципала на каждом запросе.
            // Уже существующий обработчик (если он есть) сохраняем и вызываем первым.
            services.PostConfigure<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme, options =>
                {
                    var previous = options.Events.OnValidatePrincipal;

                    options.Events.OnValidatePrincipal = async context =>
                    {
                        if (previous is not null)
                        {
                            await previous(context);
                        }

                        if (context.Principal is null)
                        {
                            return; // предыдущий обработчик уже отклонил принципал
                        }

                        await SyncTokensAsync(context);
                    };
                });

            return services;
        }

        private static async Task SyncTokensAsync(CookieValidatePrincipalContext context)
        {
            var properties = context.Properties;
            var accessToken = properties.GetTokenValue("access_token");
            var refreshToken = properties.GetTokenValue("refresh_token");

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
            {
                return;
            }

            var services = context.HttpContext.RequestServices;
            var store = services.GetRequiredService<UserTokenStore>();
            var refresher = services.GetRequiredService<TokenRefresher>();
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Messenger.Web.TokenRefresh");

            // Первый запрос после входа (или cookie от старой версии): выдаём сессии ключ.
            properties.Items.TryGetValue(StoreKeyItem, out var key);
            var isNewKey = string.IsNullOrEmpty(key);
            if (isNewKey)
            {
                key = Guid.NewGuid().ToString("N");
                properties.Items[StoreKeyItem] = key;
            }

            // После перезапуска приложения хранилище пустое — регистрируем токены из cookie заново.
            var entry = store.GetOrAdd(key!, accessToken, refreshToken);
            entry.LastSeenUtc = DateTime.UtcNow;

            if (isNewKey)
            {
                logger.LogInformation(
                    "[TokenRefresh] Новая сессия {Key} зарегистрирована, access-токен истекает в {ExpiresAt:HH:mm:ss} UTC",
                    key, entry.Tokens.ExpiresAtUtc);
            }

            // Запасной путь: фоновый сервис не успел (например, сразу после перезапуска).
            await refresher.RefreshIfNeededAsync(entry, context.HttpContext.RequestAborted);

            if (entry.IsRevoked)
            {
                // Refresh-токен отклонён SSO — принудительный выход.
                logger.LogWarning("[TokenRefresh] Сессия {Key}: refresh-токен отклонён — выполняем выход", key);
                store.Remove(key!);
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var tokens = entry.Tokens;
            if (isNewKey || tokens.AccessToken != accessToken || tokens.RefreshToken != refreshToken)
            {
                properties.UpdateTokenValue("access_token", tokens.AccessToken);
                properties.UpdateTokenValue("refresh_token", tokens.RefreshToken);
                context.ShouldRenew = true; // перевыпустить cookie с новыми токенами

                if (!isNewKey)
                {
                    logger.LogInformation(
                        "[TokenRefresh] Сессия {Key}: cookie обновлена свежими токенами (истекают в {ExpiresAt:HH:mm:ss} UTC)",
                        key, tokens.ExpiresAtUtc);
                }
            }
        }
    }
}