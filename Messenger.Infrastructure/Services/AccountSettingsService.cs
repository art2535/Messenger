using Messenger.Application.DTOs.AccountSettings;
using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Messenger.Infrastructure.Services
{
    public class AccountSettingsService : IAccountSettingsService
    {
        private readonly GuapMessengerContext _context;

        public AccountSettingsService(GuapMessengerContext context)
        {
            _context = context;
        }

        public async Task<AccountSettingsDto> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var settings = await _context.AccountSettings
                .FirstOrDefaultAsync(x => x.AccountId == userId, cancellationToken);

            if (settings == null)
            {
                settings = new AccountSetting { AccountId = userId };
                _context.AccountSettings.Add(settings);
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (
                    ex.InnerException is PostgresException
                    {
                        SqlState: PostgresErrorCodes.UniqueViolation
                    })
                {
                    // Другой запрос мог создать настройки между SELECT и INSERT.
                    _context.Entry(settings).State = EntityState.Detached;
                    settings = await _context.AccountSettings
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.AccountId == userId, cancellationToken)
                        ?? throw new InvalidOperationException("Не удалось получить созданные настройки аккаунта");
                }

                return Map(settings);
            }

            // Нулевые значения privacy-настроек могут быть осознанным выбором пользователя.
            // Нормализуем только отсутствующую тему и не перезаписываем остальные настройки.
            if (string.IsNullOrWhiteSpace(settings.Theme))
            {
                settings.Theme = "system";
                await _context.SaveChangesAsync(cancellationToken);
            }

            return Map(settings);
        }

        public async Task<AccountSettingsDto> UpdateAsync(Guid userId, UpdateAccountSettingsRequest request,
            CancellationToken cancellationToken = default)
        {
            var settings = await _context.AccountSettings
                .FirstOrDefaultAsync(x => x.AccountId == userId, cancellationToken);

            if (settings == null)
            {
                settings = new AccountSetting { AccountId = userId };
                _context.AccountSettings.Add(settings);
            }

            ApplyUpdate(settings, request);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation
                } && _context.Entry(settings).State == EntityState.Added)
            {
                // Параллельный запрос уже создал строку настроек.
                _context.Entry(settings).State = EntityState.Detached;
                settings = await _context.AccountSettings
                    .FirstOrDefaultAsync(x => x.AccountId == userId, cancellationToken)
                    ?? throw new InvalidOperationException("Не удалось получить созданные настройки аккаунта");

                ApplyUpdate(settings, request);
                await _context.SaveChangesAsync(cancellationToken);
            }

            return Map(settings);
        }

        private static void ApplyUpdate(AccountSetting settings, UpdateAccountSettingsRequest request)
        {
    settings.PushEnabled = request.PushEnabled;
    settings.NotifyMessages = request.NotifyMessages;
    settings.NotifyGroupChats = request.NotifyGroupChats;
    settings.NotifyMentions = request.NotifyMentions;
    settings.NotifyReactions = request.NotifyReactions;
    settings.NotifySystem = request.NotifySystem;
    settings.NotifySound = request.NotifySound;
    settings.SuppressOpenChat = request.SuppressOpenChat;
    settings.NotifyPreview = request.NotifyPreview;
    settings.Theme = NormalizeTheme(request.Theme);
    settings.LargeFont = request.LargeFont;
    settings.CompactList = request.CompactList;
    settings.ShowOnline = request.ShowOnline;
    settings.ShowTyping = request.ShowTyping;
    settings.ReadReceipts = request.ReadReceipts;
    settings.IdleLogout = request.IdleLogout;
        }

        private static string NormalizeTheme(string? theme)
        {
            theme = (theme ?? "system").Trim().ToLowerInvariant();
            return theme is "light" or "dark" or "system" ? theme : "system";
        }

        private static AccountSettingsDto Map(AccountSetting s) => new()
        {
            PushEnabled = s.PushEnabled,
            NotifyMessages = s.NotifyMessages,
            NotifyGroupChats = s.NotifyGroupChats,
            NotifyMentions = s.NotifyMentions,
            NotifyReactions = s.NotifyReactions,
            NotifySystem = s.NotifySystem,
            NotifySound = s.NotifySound,
            SuppressOpenChat = s.SuppressOpenChat,
            NotifyPreview = s.NotifyPreview,
            Theme = s.Theme,
            LargeFont = s.LargeFont,
            CompactList = s.CompactList,
            ShowOnline = s.ShowOnline,
            ShowTyping = s.ShowTyping,
            ReadReceipts = s.ReadReceipts,
            IdleLogout = s.IdleLogout
        };
    }
}
