using System.ComponentModel.DataAnnotations;

namespace Messenger.Core.Configuration;

public sealed class SignalROptions
{
    public const string SectionName = "SignalR";
    public bool EnableDetailedErrors { get; set; }

    [Required, MinLength(1), MaxLength(128)]
    public string ChannelPrefix { get; set; } = "Messenger";

    [Range(1024, 1048576)]
    public int MaximumReceiveMessageSize { get; set; } = 64 * 1024;

    [Range(5, 300)]
    public int ClientTimeoutSeconds { get; set; } = 30;

    [Range(1, 150)]
    public int KeepAliveSeconds { get; set; } = 15;
}
