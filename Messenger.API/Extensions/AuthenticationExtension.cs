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
                            ValidAudiences = [
                                configuration["AzureAd:Audience"] ?? "messager",
                                "account"
                            ],

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
                                {
                                    context.Fail("Access token does not contain a subject.");
                                }

                                return Task.CompletedTask;
                            }
                        };
                    });

                return services;
            }
        }
    }
}