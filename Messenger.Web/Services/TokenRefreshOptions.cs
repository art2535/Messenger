using System.ComponentModel.DataAnnotations;

namespace Messenger.Web.Services
{
    public sealed class TokenRefreshOptions
    {
        public const string SectionName = "TokenRefresh";

        [Range(1, 3600)]
        public int CheckIntervalSeconds { get; set; } = 20;

        [Range(1, 3600)]
        public int RefreshBeforeExpirySeconds { get; set; } = 90;

        [Range(1, 10080)]
        public int IdleTimeoutMinutes { get; set; } = 30;
    }
}