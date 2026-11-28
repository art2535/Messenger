using System.ComponentModel.DataAnnotations;

namespace Messenger.Core.Configuration;

public sealed class IdentityProviderOptions
{
    public const string SectionName = "AzureAd";

    [Required, Url]
    public string Instance { get; set; } = string.Empty;

    [Required]
    public string TenantId { get; set; } = string.Empty;

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = "messager";

    public string? ClientSecret { get; set; }
    public string CallbackPath { get; set; } = "/signin-oidc";
    public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";
    public string Authority => $"{Instance?.TrimEnd('/')}/{TenantId}";
    public string OpenIdConnectBase => $"{Authority}/protocol/openid-connect";
}
