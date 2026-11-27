using Messenger.Core.Models;
using Messenger.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Messenger.Infrastructure.Services
{
    public class JwtService
    {
        private readonly IConfiguration _configuration;
        private readonly GuapMessengerContext _context;

        public JwtService(IConfiguration configuration, GuapMessengerContext context)
        {
            _configuration = configuration;
            _context = context;
        }

        public async Task<string> GenerateJwtTokenAsync(User user, CancellationToken token = default)
        {
            // Process-level environment variables work consistently in containers,
            // Linux services and Windows hosts. Configuration remains the fallback.
            string? key = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
                ?? _configuration["Jwt:Key"];

            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("JWT ключ не найден для создания токена.");
            }

            byte[] keyBytes;
            try
            {
                keyBytes = Convert.FromBase64String(key);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("JWT ключ не является корректной Base64 строкой.");
            }

            if (keyBytes.Length < 32)
                throw new InvalidOperationException("JWT ключ должен содержать не менее 32 байт после декодирования Base64.");

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Login ?? user.UserId.ToString())
            };

            var userRoles = await _context.Users
                .Where(u => u.UserId == user.UserId)
                .SelectMany(u => u.Roles)
                .ToListAsync(token);
            foreach (var role in userRoles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.Name));
            }

            var creds = new SigningCredentials(
                new SymmetricSecurityKey(keyBytes),
                SecurityAlgorithms.HmacSha256);

            var jwtToken = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(jwtToken);
        }
    }
}
