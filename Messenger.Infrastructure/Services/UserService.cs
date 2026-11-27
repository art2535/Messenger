using Messenger.Application.DTOs.Users;
using Messenger.Application.Files;
using Messenger.Application.Interfaces;
using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

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
                try
                {
                    await _context.SaveChangesAsync(token);
                }
                catch (DbUpdateException ex) when (
                    ex.InnerException is PostgresException
                    {
                        SqlState: PostgresErrorCodes.UniqueViolation
                    })
                {
                    // Связь уже могла быть добавлена параллельным запросом.
                    var trackedEntry = _context.ChangeTracker.Entries()
                        .FirstOrDefault(item => ReferenceEquals(item.Entity, entry));
                    if (trackedEntry is not null)
                        trackedEntry.State = EntityState.Detached;
                }
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
            ArgumentNullException.ThrowIfNull(file);

            const long maxFileSize = 2 * 1024 * 1024;
            if (file.Length <= 0 || file.Length > maxFileSize)
                throw new ArgumentException("Размер изображения должен быть больше 0 и не превышать 2 МБ");

            var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            if (!allowedExtensions.Contains(fileExtension))
                throw new ArgumentException("Неподдерживаемый формат изображения");

            var baseUrl = _configuration["URL:API:HTTPS"]
                ?? throw new InvalidOperationException("URL API не указан в конфигурационном файле");
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var apiBaseUri))
                throw new InvalidOperationException("URL API имеет некорректный формат");

            var user = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.UserId == userId, token);

            if (user == null)
                throw new UnauthorizedAccessException("Пользователь не найден");

            await using var sourceStream = file.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await sourceStream.CopyToAsync(memoryStream, token);

            if (memoryStream.Length == 0 || memoryStream.Length > maxFileSize)
                throw new ArgumentException("Размер изображения должен быть больше 0 и не превышать 2 МБ");

            var imageBytes = memoryStream.ToArray();
            if (!HasValidImageSignature(imageBytes, fileExtension))
                throw new ArgumentException("Содержимое файла не соответствует формату изображения");

            var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadsFolder = Path.Combine(webRoot, "avatars");
            Directory.CreateDirectory(uploadsFolder);

            var oldFilePath = user.Account == null
                ? null
                : TryGetAvatarFilePath(user.Account.Avatar, apiBaseUri, webRoot);

            var fileName = $"{userId}_{Guid.NewGuid():N}{fileExtension}";
            var filePath = Path.Combine(uploadsFolder, fileName);
            var avatarUrl = $"{baseUrl.TrimEnd('/')}/avatars/{fileName}";

            try
            {
                await File.WriteAllBytesAsync(filePath, imageBytes, token);

                if (user.Account == null)
                {
                    user.Account = new AccountSetting { AccountId = userId };
                    _context.AccountSettings.Add(user.Account);
                }

                user.Account.Avatar = avatarUrl;
                await _context.SaveChangesAsync(token);
            }
            catch
            {
                TryDeleteFile(filePath);
                throw;
            }

            // Старый файл удаляется только после фиксации новой ссылки в БД.
            if (oldFilePath != null && !string.Equals(oldFilePath, filePath, StringComparison.Ordinal))
                TryDeleteFile(oldFilePath);

            return avatarUrl;
        }

        public async Task DeleteAvatarAsync(Guid userId, CancellationToken token = default)
        {
            var user = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.UserId == userId, token);

            if (user?.Account == null || string.IsNullOrWhiteSpace(user.Account.Avatar))
                return;

            var baseUrl = _configuration["URL:API:HTTPS"];
            Uri? apiBaseUri = Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUri)
                ? parsedBaseUri
                : null;
            var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var oldFilePath = apiBaseUri == null
                ? null
                : TryGetAvatarFilePath(user.Account.Avatar, apiBaseUri, webRoot);

            user.Account.Avatar = null;
            await _context.SaveChangesAsync(token);

            // После успешного обновления БД ошибка удаления оставит только orphan-файл,
            // но не сломанную ссылку на изображение.
            if (oldFilePath != null)
                TryDeleteFile(oldFilePath);
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

        private static bool HasValidImageSignature(byte[] bytes, string extension)
        {
            return extension switch
            {
                ".jpg" or ".jpeg" => bytes.Length >= 3
                    && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
                ".png" => bytes.Length >= 8
                    && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                    && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A,
                ".gif" => bytes.Length >= 6
                    && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46
                    && bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61,
                ".webp" => bytes.Length >= 12
                    && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                    && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50,
                _ => false
            };
        }

        private static string? TryGetAvatarFilePath(string? avatar, Uri apiBaseUri, string webRoot)
        {
            if (string.IsNullOrWhiteSpace(avatar) || avatar.Contains("default", StringComparison.OrdinalIgnoreCase))
                return null;

            string path;
            if (Uri.TryCreate(avatar, UriKind.Absolute, out var avatarUri))
            {
                if (!string.Equals(avatarUri.Scheme, apiBaseUri.Scheme, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(avatarUri.Host, apiBaseUri.Host, StringComparison.OrdinalIgnoreCase)
                    || avatarUri.Port != apiBaseUri.Port)
                    return null;

                path = avatarUri.AbsolutePath;
            }
            else
            {
                path = avatar;
            }

            path = Uri.UnescapeDataString(path).Replace('\\', '/').TrimStart('/');
            if (!path.StartsWith("avatars/", StringComparison.OrdinalIgnoreCase))
                return null;

            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
                return null;

            var uploadsFolder = Path.GetFullPath(Path.Combine(webRoot, "avatars"));
            var filePath = Path.GetFullPath(Path.Combine(uploadsFolder, fileName));
            return filePath.StartsWith(uploadsFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                ? filePath
                : null;
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (IOException)
            {
                // Cleanup failure leaves an orphan file; the database reference stays consistent.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup failure leaves an orphan file; the database reference stays consistent.
            }
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
