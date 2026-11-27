namespace Messenger.Application.DTOs.Users
{
    public class UserProfileDto
    {
        public string? UserId { get; set; }
        public string? LastName { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? Login { get; set; }
        public string? Phone { get; set; }
        public AccountDto? Account { get; set; }
    }
}
