using Messenger.Core.DTOs.AccountSettings;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.AccountId == userId, cancellationToken);

            if (settings == null)
            {
                settings = new AccountSetting { AccountId = userId };
                _context.AccountSettings.Add(settings);
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

            await _context.SaveChangesAsync(cancellationToken);
            return Map(settings);
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
