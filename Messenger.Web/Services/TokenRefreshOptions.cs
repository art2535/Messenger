namespace Messenger.Web.Services
{
    public sealed class TokenRefreshOptions
    {
        public int CheckIntervalSeconds { get; set; } = 20;
        public int RefreshBeforeExpirySeconds { get; set; } = 90;
        public int IdleTimeoutMinutes { get; set; } = 480;
    }
}