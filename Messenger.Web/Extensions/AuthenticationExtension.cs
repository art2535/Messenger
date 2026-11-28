using Messenger.Core.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Messenger.Web.Extensions
{
    public static class AuthenticationExtension
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddEtaWebAuthentication(IConfiguration configuration)
            {
                var identityProvider = configuration.GetSection(IdentityProviderOptions.SectionName)
                    .Get<IdentityProviderOptions>() ?? new IdentityProviderOptions();

                services.AddOptions<IdentityProviderOptions>()
                    .Bind(configuration.GetSection(IdentityProviderOptions.SectionName))
                    .ValidateDataAnnotations()
                    .Validate(options => !string.IsNullOrWhiteSpace(options.ClientSecret),
                        "AzureAd:ClientSecret обязателен для Web OIDC-клиента.")
                    .Validate(options => Uri.TryCreate(options.Instance, UriKind.Absolute, out var uri)
                        && uri.Scheme == Uri.UriSchemeHttps,
                        "AzureAd:Instance должен быть абсолютным HTTPS URL.")
                    .ValidateOnStart();

                services.AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                    options.DefaultSignOutScheme = OpenIdConnectDefaults.AuthenticationScheme;
                    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                })
                .AddCookie(options =>
                {
                    options.LoginPath = "/login";
                    options.ExpireTimeSpan = TimeSpan.FromHours(12);
                    options.SlidingExpiration = true;
                    options.Cookie.Name = ".GuapMessenger.Cookie";
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.Cookie.IsEssential = true;
                    options.Cookie.Path = "/";
                    options.Cookie.MaxAge = TimeSpan.FromHours(12);
                })
                .AddOpenIdConnect(options =>
                {
                    options.Authority = identityProvider.Authority;
                    options.ClientId = identityProvider.ClientId;
                    options.ClientSecret = identityProvider.ClientSecret;
                    options.CallbackPath = identityProvider.CallbackPath;
                    options.SignedOutCallbackPath = identityProvider.SignedOutCallbackPath;
                    options.ResponseType = "code";
                    options.SaveTokens = true;
                    options.GetClaimsFromUserInfoEndpoint = false;
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                    options.Scope.Add("email");
                    options.Scope.Add("roles");
                    options.Scope.Add("offline_access");
                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.NameClaimType = "name";
                    options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;
                    options.UseTokenLifetime = true;

                    options.Events = new OpenIdConnectEvents
                    {
                        OnMessageReceived = context =>
                        {
                            var accessToken = context.Request.Query["access_token"];
                            if (!string.IsNullOrEmpty(accessToken) &&
                                context.HttpContext.Request.Path.StartsWithSegments("/api"))
                                context.Token = accessToken;
                            return Task.CompletedTask;
                        },
                        OnAuthenticationFailed = _ => Task.CompletedTask,
                        OnTokenValidated = context =>
                        {
                            var accessToken = context.TokenEndpointResponse?.AccessToken;
                            if (string.IsNullOrEmpty(accessToken))
                                return Task.CompletedTask;

                            var identity = context.Principal?.Identity as ClaimsIdentity;
                            if (identity is null)
                                return Task.CompletedTask;

                            try
                            {
                                var parts = accessToken.Split('.');
                                if (parts.Length < 2)
                                    return Task.CompletedTask;

                                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                                payload += (payload.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
                                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                                using var doc = JsonDocument.Parse(json);

                                if (doc.RootElement.TryGetProperty("realm_access", out var realmAccess) &&
                                    realmAccess.TryGetProperty("roles", out var rolesElement) &&
                                    rolesElement.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var roleElement in rolesElement.EnumerateArray())
                                    {
                                        var role = roleElement.GetString();
                                        if (!string.IsNullOrWhiteSpace(role) && !identity.HasClaim(ClaimTypes.Role, role))
                                            identity.AddClaim(new Claim(ClaimTypes.Role, role));
                                    }
                                }
                            }
                            catch (JsonException) { }
                            catch (FormatException) { }

                            return Task.CompletedTask;
                        }
                    };
                });

                return services;
            }
        }
    }
}