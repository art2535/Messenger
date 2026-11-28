using System.ComponentModel.DataAnnotations;

namespace Messenger.Core.Configuration;

public sealed class VapidOptions
{
    public const string SectionName = "Vapid";

    [Required]
    public string Subject { get; set; } = string.Empty;

    [Required, MinLength(32)]
    public string PublicKey { get; set; } = string.Empty;

    [Required, MinLength(32)]
    public string PrivateKey { get; set; } = string.Empty;
}
