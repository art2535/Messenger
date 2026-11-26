namespace Messenger.Core.DTOs.Admin
{
    public class AdminActivityStatsDto
    {
        public int TotalUsers { get; set; }
        public int OnlineUsers { get; set; }
        public int MessagesLast24h { get; set; }
        public int MessagesLast7d { get; set; }
        public int ActiveChatsLast24h { get; set; }
        public int PendingReports { get; set; }
        public int ActiveMutes { get; set; }
        public List<DailyCountDto> MessagesPerDay { get; set; } = new();
        public List<TopChatDto> TopChats { get; set; } = new();
    }

    public class DailyCountDto
    {
        public DateOnly Date { get; set; }
        public int Count { get; set; }
    }

    public class TopChatDto
    {
        public Guid ChatId { get; set; }
        public string? ChatName { get; set; }
        public int MessageCount { get; set; }
    }
}
