namespace Messenger.Infrastructure.Persistence.Entities;

public sealed class Log
{
    public Guid Id { get; set; }
    public string? Message { get; set; }
    public string? MessageTemplate { get; set; }
    public string? Level { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Exception { get; set; }
    public string? LogEvent { get; set; }
    public string? Application { get; set; }
    public string? MachineName { get; set; }
    public string? Environment { get; set; }
}
