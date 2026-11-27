namespace Messenger.Application.DTOs.Users
{
    public class UserSearch
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Avatar { get; set; }
    }
}
