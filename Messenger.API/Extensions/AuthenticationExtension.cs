using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Messenger.API.Extensions
{
    public static class AuthenticationExtension
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddEtaApiAuthentication(IConfiguration configuration, bool requireHttpsMetadata)
            {
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.Authority = $"{configuration["AzureAd:Instance"]?.TrimEnd('/')}/{configuration["AzureAd:TenantId"]}";
                        options.RequireHttpsMetadata = requireHttpsMetadata;

                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = $"{configuration["AzureAd:Instance"]?.TrimEnd('/')}/{configuration["AzureAd:TenantId"]}",

                            ValidateAudience = true,
                            ValidAudiences = ["messager", "account"],

                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.FromMinutes(10),

                            NameClaimType = "sub",
                            RoleClaimType = "role",

                            ValidateIssuerSigningKey = true
                        };

                        options.MapInboundClaims = false;
                    });

                return services;
            }

            public IServiceCollection AddEtaWebAuthentication(IConfiguration configuration)
            {
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
                    options.LoginPath = "/Authorization";
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
                    options.Authority = $"{configuration["AzureAd:Instance"]?.TrimEnd('/')}/{configuration["AzureAd:TenantId"]}";
                    options.ClientId = configuration["AzureAd:ClientId"];
                    options.ClientSecret = configuration["AzureAd:ClientSecret"];
                    options.CallbackPath = configuration["AzureAd:CallbackPath"];
                    options.SignedOutCallbackPath = configuration["AzureAd:SignedOutCallbackPath"];
                    options.ResponseType = "code";
                    options.SaveTokens = true;
                    options.GetClaimsFromUserInfoEndpoint = true;

                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                    options.Scope.Add("email");

                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.NameClaimType = "name";
                    options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;

                    options.UseTokenLifetime = true;

                    options.Events = new OpenIdConnectEvents
                    {
                        OnTokenValidated = context =>
                        {
                            var accessToken = context.TokenEndpointResponse?.AccessToken;
                            if (string.IsNullOrEmpty(accessToken))
                                return Task.CompletedTask;

                            var identity = context.Principal?.Identity as ClaimsIdentity;
                            if (identity == null)
                                return Task.CompletedTask;

                            try
                            {
                                var parts = accessToken.Split('.');
                                if (parts.Length < 2)
                                    return Task.CompletedTask;

                                var payload = parts[1]
                                    .Replace('-', '+')
                                    .Replace('_', '/');

                                switch (payload.Length % 4)
                                {
                                    case 2: payload += "=="; break;
                                    case 3: payload += "="; break;
                                }

                                var jsonBytes = Convert.FromBase64String(payload);
                                var json = Encoding.UTF8.GetString(jsonBytes);

                                using var doc = JsonDocument.Parse(json);

                                if (doc.RootElement.TryGetProperty("realm_access", out var realmAccess) &&
                                    realmAccess.TryGetProperty("roles", out var rolesElement) &&
                                    rolesElement.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var roleElement in rolesElement.EnumerateArray())
                                    {
                                        var role = roleElement.GetString();
                                        if (string.IsNullOrEmpty(role))
                                            continue;

                                        if (!identity.HasClaim(ClaimTypes.Role, role))
                                        {
                                            identity.AddClaim(new Claim(ClaimTypes.Role, role));
                                        }
                                    }
                                }
                            }
                            catch { }

                            return Task.CompletedTask;
                        }
                    };
                });

                return services;
            }
        }
    }
}