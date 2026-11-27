namespace Messenger.Application.DTOs.Auth
{
    public class LoginEtaResponse
    {
        public bool IsSuccess { get; set; }
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
    }
}
