using Messenger.Core.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Messenger.API.Extensions
{
    public static class AuthenticationExtension
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddEtaApiAuthentication(IConfiguration configuration, bool requireHttpsMetadata)
            {
                var identityProvider = configuration.GetSection(IdentityProviderOptions.SectionName)
                    .Get<IdentityProviderOptions>() ?? new IdentityProviderOptions();

                services.AddOptions<IdentityProviderOptions>()
                    .Bind(configuration.GetSection(IdentityProviderOptions.SectionName))
                    .ValidateDataAnnotations()
                    .Validate(options => Uri.TryCreate(options.Instance, UriKind.Absolute, out var uri)
                        && (uri.Scheme == Uri.UriSchemeHttps || !requireHttpsMetadata),
                        "AzureAd:Instance должен быть абсолютным HTTPS URL вне Development.")
                    .ValidateOnStart();

                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.Authority = identityProvider.Authority;
                        options.RequireHttpsMetadata = requireHttpsMetadata;
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = identityProvider.Authority,
                            ValidateAudience = true,
                            ValidAudiences = [identityProvider.Audience, "account"],
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.FromMinutes(2),
                            NameClaimType = "sub",
                            RoleClaimType = "role",
                            ValidateIssuerSigningKey = true
                        };

                        options.MapInboundClaims = false;
                        options.Events = new JwtBearerEvents
                        {
                            OnTokenValidated = context =>
                            {
                                var subject = context.Principal?.FindFirst("sub")?.Value;
                                if (string.IsNullOrWhiteSpace(subject))
                                    context.Fail("Access token does not contain a subject.");
                                return Task.CompletedTask;
                            }
                        };
                    });

                return services;
            }
        }
    }
}