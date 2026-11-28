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
        private readonly IFileStorage _fileStorage;
        private readonly IFileValidationService _fileValidationService;

        private static readonly Dictionary<string, string> RoleDisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ROLE_ENTRANT"] = "Абитуриент",
            ["ROLE_STUDENT"] = "Студент",
            ["ROLE_TEACHER"] = "Преподаватель",
            ["ROLE_EMPLOYEE"] = "Сотрудник",
            ["ROLE_ADMIN"] = "Администратор",
            ["ROLE_USER"] = "Пользователь"
        };

        public UserService(
            GuapMessengerContext context,
            IConfiguration configuration,
            IFileStorage fileStorage,
            IFileValidationService fileValidationService)
        {
            _context = context;
            _configuration = configuration;
            _fileStorage = fileStorage;
            _fileValidationService = fileValidationService;
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
            var validated = await _fileValidationService.ValidateAsync(file, FileUploadPurpose.Avatar, token);
            const long maxFileSize = 2 * 1024 * 1024;

            var baseUrl = _configuration["URL:API:HTTPS"]
                ?? throw new InvalidOperationException("URL API не указан в конфигурационном файле");
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
                throw new InvalidOperationException("URL API имеет некорректный формат");

            var user = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.UserId == userId, token);

            if (user == null)
                throw new UnauthorizedAccessException("Пользователь не найден");

            var fileName = $"{userId}_{Guid.NewGuid():N}{validated.Extension}";
            var avatarUrl = $"{baseUrl.TrimEnd('/')}/avatars/{fileName}";

            try
            {
                await using var sourceStream = file.OpenReadStream();
                var storedLength = await _fileStorage.SaveAsync(
                    FileStorageArea.Avatars, fileName, sourceStream, maxFileSize, token);
                if (storedLength != validated.Length)
                    throw new InvalidDataException("Размер загруженного аватара не совпадает с заявленным размером");

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
                await TryDeleteStoredFileAsync(FileStorageArea.Avatars, fileName);
                throw;
            }


            return avatarUrl;
        }

        public async Task DeleteAvatarAsync(Guid userId, CancellationToken token = default)
        {
            var user = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.UserId == userId, token);

            if (user?.Account == null || string.IsNullOrWhiteSpace(user.Account.Avatar))
                return;


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
            CancellationToken token = default)
        {
            var user = await _context.Users
                .Include(u => u.Account)
                .FirstOrDefaultAsync(u => u.UserId == userId, token);

            if (user == null)
                throw new UnauthorizedAccessException("Пользователь не найден");

            user.FirstName = request.FirstName;
            user.LastName = request.LastName;
            user.Login = request.Login;


            _context.Users.Update(user);
            await _context.SaveChangesAsync(token);
        }

        public async Task<UserDto?> GetUserByExternalIdAsync(string externalId, CancellationToken token = default)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Account)
                .Include(u => u.Roles)
                .FirstOrDefaultAsync(u => u.ExternalId == externalId, token);

            return user == null ? null : MapToDto(user);
        }

        public async Task<UserDto> RegisterExternalUserAsync(string externalId, string email, string firstName,
            string lastName, IEnumerable<string>? tokenRoles = null, CancellationToken token = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
            externalId = externalId.Trim();
            if (externalId.Length > 255)
                throw new ArgumentException("Внешний идентификатор не должен превышать 255 символов", nameof(externalId));

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

            // Уникальный индекс External_ID является окончательной защитой от
            // параллельной регистрации одного внешнего пользователя.
            _context.Users.Add(user);
            try
            {
                await _context.SaveChangesAsync(token);
                return MapToDto(user);
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: "IX_Users_External_ID"
                })
            {
                // INSERT проиграл конкурентной регистрации. Отсоединяем только
                // добавленные сущности проигравшего графа, не затрагивая другие изменения.
                foreach (var entry in _context.ChangeTracker.Entries().ToList())
                {
                    if (ReferenceEquals(entry.Entity, user)
                        || ReferenceEquals(entry.Entity, user.Account)
                        || ReferenceEquals(entry.Entity, user.UserStatus))
                    {
                        entry.State = EntityState.Detached;
                        continue;
                    }

                    if (entry.State == EntityState.Added && entry.Metadata.Name == "UserRole"
                        && (Guid)entry.Property("UserId").CurrentValue! == userId)
                    {
                        entry.State = EntityState.Detached;
                    }
                }

                var concurrentUser = await GetUserByExternalIdAsync(externalId, token);
                if (concurrentUser is not null)
                    return concurrentUser;

                // Нарушение уникальности не по External_ID или запись была удалена
                // до повторного чтения — исходную ошибку нельзя скрывать.
                throw;
            }
        }

        private async Task TryDeleteStoredFileAsync(FileStorageArea area, string fileName)
        {
            try
            {
                await _fileStorage.DeleteAsync(area, fileName, CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Неуспешная очистка оставляет orphan-файл; ссылку в БД уже не восстанавливаем.
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
