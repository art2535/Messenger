using Messenger.Core.DTOs.Users;
using Messenger.Core.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Messenger.Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Messenger.Infrastructure.Services
{
    public class UserService : IUserService
    {
        private readonly UserRepository _userRepository;
        private readonly GuapMessengerContext _context;
        private readonly IConfiguration _configuration;

        private static readonly Dictionary<string, string> RoleDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ROLE_ENTRANT"] = "Абитуриент",
            ["ROLE_STUDENT"] = "Студент",
            ["ROLE_TEACHER"] = "Преподаватель",
            ["ROLE_EMPLOYEE"] = "Сотрудник",
            ["ROLE_ADMIN"] = "Администратор",
            ["ROLE_USER"] = "Пользователь"
        };

        public UserService(UserRepository userRepository, GuapMessengerContext context,
            IConfiguration configuration)
        {
            _userRepository = userRepository;
            _context = context;
            _configuration = configuration;
        }

        public async Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken token = default)
        {
            await _userRepository.AssignUserRoleAsync(userId, roleId, token);
        }

        public async Task BlockUserAsync(Guid userId, Guid blockedUserId, CancellationToken token = default)
        {
            await _userRepository.AddUserToBlacklistAsync(userId, blockedUserId, token);
        }

        public async Task DeleteAccountAsync(Guid userId, CancellationToken token = default)
        {
            await _userRepository.DeleteUserAsync(userId, token);
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync(CancellationToken token = default)
        {
            return await _userRepository.GetAllUsersAsync(token);
        }

        public async Task<IEnumerable<Role>> GetRolesAsync(CancellationToken token = default)
        {
            return await _userRepository.GetUserRolesAsync(token);
        }

        public async Task<User?> GetUserByIdAsync(Guid id, CancellationToken token = default)
        {
            return await _userRepository.GetUserByIdAsync(id, token);
        }

        public async Task<string> UploadAvatarAsync(Guid userId, IFormFile file, CancellationToken token = default)
        {
            var user = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.UserId == userId, token);

            if (user == null)
                throw new UnauthorizedAccessException("Пользователь не найден");

            if (user.Account == null)
            {
                user.Account = new AccountSetting
                {
                    SettingId = Guid.NewGuid(),
                    AccountId = userId
                };
                _context.AccountSettings.Add(user.Account);
            }

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            if (!string.IsNullOrEmpty(user.Account.Avatar))
            {
                var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.Account.Avatar.TrimStart('/'));
                if (File.Exists(oldFilePath) && !user.Account.Avatar.Contains("default"))
                {
                    File.Delete(oldFilePath);
                }
            }

            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            if (!allowedExtensions.Contains(fileExtension))
                throw new ArgumentException("Неподдерживаемый формат изображения");

            var fileName = $"{userId}{fileExtension}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream, token);
            }

            var baseUrl = _configuration["URL:API:HTTPS"]
                ?? throw new Exception("URL не указан в конфигурационном файле");

            var avatarUrl = $"{baseUrl}/avatars/{fileName}";

            user.Account.Avatar = avatarUrl;
            await _context.SaveChangesAsync(token);

            return avatarUrl;
        }

        public async Task DeleteAvatarAsync(Guid userId, CancellationToken token = default)
        {
            var user = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.UserId == userId, token);

            if (user == null || user.Account == null || string.IsNullOrEmpty(user.Account.Avatar))
                return;

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.Account.Avatar.TrimStart('/'));
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            user.Account.Avatar = null;
            await _context.SaveChangesAsync(token);
        }

        public async Task UnblockUserAsync(Guid userId, Guid blockedUserId, CancellationToken token = default)
        {
            await _userRepository.RemoveUserFromBlacklistAsync(userId, blockedUserId, token);
        }

        public async Task<IEnumerable<UserSearch>> SearchUsersAsync(string search, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(search))
                return Enumerable.Empty<UserSearch>();

            search = search.ToLower().Trim();
            string searchPattern = $"%{search}%";

            var usersData = await _context.Users
                .AsNoTracking()
                .Include(u => u.Account)
                .Where(u =>
                    EF.Functions.Like((u.FirstName ?? "").ToLower(), searchPattern) ||
                    EF.Functions.Like((u.LastName ?? "").ToLower(), searchPattern) ||
                    EF.Functions.Like((u.Login ?? "").ToLower(), searchPattern) ||
                    EF.Functions.Like(((u.FirstName + " " + u.LastName) ?? "").ToLower(), searchPattern) ||
                    EF.Functions.Like(((u.LastName + " " + u.FirstName) ?? "").ToLower(), searchPattern)
                )
                .OrderBy(u => u.LastName)
                .ThenBy(u => u.FirstName)
                .Take(15)
                .ToListAsync(token);

            return usersData
                .Select(u => new UserSearch
                {
                    Id = u.UserId,
                    Name = string.Join(" ", new[] { u.FirstName, u.LastName }.Where(s => !string.IsNullOrEmpty(s))),
                    Avatar = u.Account?.Avatar
                })
                .ToList();
        }

        public async Task<IEnumerable<User>> GetBlockedUsersAsync(Guid userId, CancellationToken token = default)
        {
            return await _context.Blacklists
                .AsNoTracking()
                .Where(b => b.UserId == userId)
                .Include(b => b.BlockedUser)
                    .ThenInclude(u => u.Account)
                .Select(b => b.BlockedUser!)
                .ToListAsync(token);
        }

        public async Task UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request,
            string? avatarUrl = null, CancellationToken token = default)
        {
            var user = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.UserId == userId, token);

            if (user == null)
                throw new UnauthorizedAccessException("Пользователь не найден");

            user.FirstName = request.FirstName;
            user.LastName = request.LastName;
            user.Login = request.Login;

            if (!string.IsNullOrEmpty(avatarUrl) && user.Account != null)
                user.Account.Avatar = avatarUrl;

            _context.Users.Update(user);
            await _context.SaveChangesAsync(token);
        }

        public async Task<bool> IsBlockedByAsync(Guid blockerId, Guid blockedId, CancellationToken token = default)
        {
            return await _context.Blacklists
                .AnyAsync(ub => ub.UserId == blockerId && ub.BlockedUserId == blockedId, token);
        }

        public async Task<User?> GetUserByExternalIdAsync(string externalId)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.ExternalId == externalId);
        }

        public async Task<User> RegisterExternalUserAsync(string externalId, string email, string firstName,
            string lastName, IEnumerable<string>? tokenRoles = null)
        {
            var existing = await GetUserByExternalIdAsync(externalId);
            if (existing != null)
                return existing;

            var userId = Guid.NewGuid();

            var user = new User
            {
                UserId = userId,
                ExternalId = externalId,
                Login = email,
                FirstName = firstName,
                LastName = lastName,
                RegistrationDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Account = new AccountSetting
                {
                    SettingId = Guid.NewGuid(),
                    AccountId = userId
                },
                UserStatus = new UserStatus
                {
                    UserId = userId,
                    Online = true
                }
            };

            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();

            await AssignRolesFromTokenAsync(userId, tokenRoles);

            return user;
        }

        private async Task AssignRolesFromTokenAsync(Guid userId, IEnumerable<string>? tokenRoles)
        {
            var rolesToAssign = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (tokenRoles != null)
            {
                foreach (var tokenRole in tokenRoles)
                {
                    if (RoleDisplayNames.TryGetValue(tokenRole, out var displayName))
                    {
                        rolesToAssign.Add(displayName);
                    }
                }
            }

            if (rolesToAssign.Count == 0)
            {
                rolesToAssign.Add("Пользователь");
            }

            foreach (var roleName in rolesToAssign)
            {
                var role = await _context.Roles
                    .FirstOrDefaultAsync(r => r.Name == roleName);

                if (role != null)
                {
                    await AssignRoleAsync(userId, role.RoleId);
                }
            }
        }
    }
}
