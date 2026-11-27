using Messenger.Application.DTOs.Users;
using Messenger.Application.Files;
using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Messenger.Infrastructure.Services
{
    public class UserService : IUserService
    {
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

        public UserService(GuapMessengerContext context,
            IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken token = default)
        {
            var exists = await _context.Set<Dictionary<string, object>>("UserRole")
                .AnyAsync(ur => (Guid)ur["UserId"] == userId && (Guid)ur["RoleId"] == roleId, token);
            if (!exists)
            {
                var entry = new Dictionary<string, object> { ["UserId"] = userId, ["RoleId"] = roleId };
                _context.Set<Dictionary<string, object>>("UserRole").Add(entry);
                await _context.SaveChangesAsync(token);
            }
        }

        public async Task DeleteAccountAsync(Guid userId, CancellationToken token = default)
        {
            var userToDelete = await _context.Users.FindAsync([userId], token);
            if (userToDelete != null)
            {
                _context.Users.Remove(userToDelete);
                await _context.SaveChangesAsync(token);
            }
        }

        public async Task<IEnumerable<UserDto>> GetAllUsersAsync(CancellationToken token = default)
        {
            var users = await _context.Users.AsNoTracking().ToListAsync(token);
            return users.Select(MapToDto).ToList();
        }

        public async Task<IEnumerable<RoleDto>> GetRolesAsync(CancellationToken token = default)
        {
            var roles = await _context.Roles.AsNoTracking().ToListAsync(token);
            return roles.Select(role => new RoleDto
            {
                RoleId = role.RoleId,
                Name = role.Name,
                Description = role.Description
            }).ToList();
        }

        public async Task<UserDto?> GetUserByIdAsync(Guid id, CancellationToken token = default)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Account)
                .Include(u => u.Roles)
                .FirstOrDefaultAsync(u => u.UserId == id, token);

            return user == null ? null : MapToDto(user);
        }

        public async Task<string> UploadAvatarAsync(Guid userId, IUploadedFile file, CancellationToken token = default)
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
                await using var sourceStream = file.OpenReadStream();
                await sourceStream.CopyToAsync(stream, token);
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

        public async Task<UserDto?> GetUserByExternalIdAsync(string externalId, CancellationToken token = default)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.ExternalId == externalId, token);

            return user == null ? null : MapToDto(user);
        }

        public async Task<UserDto> RegisterExternalUserAsync(string externalId, string email, string firstName,
            string lastName, IEnumerable<string>? tokenRoles = null, CancellationToken token = default)
        {
            var existing = await GetUserByExternalIdAsync(externalId, token);
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
                    AccountId = userId
                },
                UserStatus = new UserStatus
                {
                    UserId = userId,
                    Online = true
                }
            };

            user.Roles = await GetRolesFromTokenAsync(tokenRoles, token);

            // Пользователь, настройки, статус и связи с ролями создаются одним сохранением.
            _context.Users.Add(user);
            await _context.SaveChangesAsync(token);

            return MapToDto(user);
        }

        private static UserDto MapToDto(User user) => new()
        {
            UserId = user.UserId,
            LastName = user.LastName,
            FirstName = user.FirstName,
            RegistrationDate = user.RegistrationDate,
            Login = user.Login,
            ExternalId = user.ExternalId,
            Account = user.Account == null ? null : new AccountDto
            {
                Avatar = user.Account.Avatar,
                Theme = user.Account.Theme
            },
            Roles = user.Roles?.Select(role => new RoleDto
            {
                RoleId = role.RoleId,
                Name = role.Name,
                Description = role.Description
            }).ToList() ?? new List<RoleDto>()
        };

        private async Task<List<Role>> GetRolesFromTokenAsync(IEnumerable<string>? tokenRoles, CancellationToken token)
        {
            var roleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (tokenRoles != null)
            {
                foreach (var tokenRole in tokenRoles)
                {
                    if (RoleDisplayNames.TryGetValue(tokenRole, out var displayName))
                        roleNames.Add(displayName);
                }
            }

            if (roleNames.Count == 0)
                roleNames.Add("Пользователь");

            return await _context.Roles
                .Where(role => roleNames.Contains(role.Name))
                .ToListAsync(token);
        }
    }
}
