namespace Messenger.Core.Responses
{
    /// <summary>
    /// Запрос внешнего входа (ЕТА ГУАП / OIDC).
    /// </summary>
    public class ExternalLoginRequest
    {
        public string ExternalId { get; set; } = null!;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
    }
}
