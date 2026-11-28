using System.ComponentModel.DataAnnotations;

namespace Messenger.Web.Services;

public sealed class WebSessionOptions
{
    public const string SectionName = "Session";

    [Range(1, 1440)]
    public int IdleTimeoutMinutes { get; set; } = 60;
}
