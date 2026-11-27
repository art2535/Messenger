namespace Messenger.Application.DTOs.Messages
{
    public record MessageSearchResponse
    {
        public bool IsSuccess { get; set; }
        public List<MessageDto> Data { get; set; } = new();
    }
}
