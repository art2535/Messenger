using System.IdentityModel.Tokens.Jwt;

namespace Messenger.Web.Services
{
    public sealed record TokenSet(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc)
    {
        public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

        public static TokenSet Create(string accessToken, string refreshToken, TimeSpan fallbackLifetime)
        {
            var expires = DateTime.UtcNow + fallbackLifetime;

            try
            {
                var validTo = new JwtSecurityTokenHandler().ReadJwtToken(accessToken).ValidTo;
                if (validTo != DateTime.MinValue)
                {
                    expires = validTo;
                }
            }
            catch (Exception)
            {
            }

            return new TokenSet(accessToken, refreshToken, expires);
        }
    }
}