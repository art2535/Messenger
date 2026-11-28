using System.ComponentModel.DataAnnotations;

namespace Messenger.Core.Configuration;

public sealed class ApiEndpointOptions
{
    public const string SectionName = "URL:API";

    [Required, Url]
    public string HTTPS { get; set; } = string.Empty;

    [Required, Url]
    public string HTTP { get; set; } = string.Empty;

    [Required, RegularExpression(@"^\d+(\.\d+)?$")]
    public string Version { get; set; } = "1.0";
}
