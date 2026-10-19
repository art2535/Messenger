using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Messenger.Web.Extensions
{
    public sealed class MetricsAuthOptions
    {
        public const string SectionName = "MetricsAuth";
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public static class MetricsProtectionExtension
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddMetricsProtection(IConfiguration configuration)
            {
                services.Configure<MetricsAuthOptions>(configuration.GetSection(MetricsAuthOptions.SectionName));
                return services;
            }
        }

        extension(WebApplication app)
        {
            public WebApplication MapProtectedPrometheusScrapingEndpoint(string path = "/metrics")
            {
                var options = app.Services.GetRequiredService<IOptions<MetricsAuthOptions>>().Value;
                var isDevelopment = app.Environment.IsDevelopment();

                var hasCredentials = !string.IsNullOrWhiteSpace(options.Username)
                                     && !string.IsNullOrWhiteSpace(options.Password);

                if (!hasCredentials)
                {
                    if (isDevelopment)
                    {
                        app.MapPrometheusScrapingEndpoint(path);
                        return app;
                    }

                    app.MapGet(path, () => Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
                    return app;
                }

                var expectedUsername = options.Username.Trim();
                var expectedPassword = options.Password;

                app.Map(path, metricsApp =>
                {
                    metricsApp.Use(async (context, next) =>
                    {
                        if (!TryAuthenticateBasic(context.Request, expectedUsername, expectedPassword))
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.Response.Headers.WWWAuthenticate = "Basic realm=\"Prometheus metrics\", charset=\"UTF-8\"";
                            await context.Response.WriteAsync("Unauthorized");
                            return;
                        }

                        await next();
                    });

                    metricsApp.UseOpenTelemetryPrometheusScrapingEndpoint();
                });

                return app;
            }
        }

        private static bool TryAuthenticateBasic(HttpRequest request, string expectedUsername, string expectedPassword)
        {
            var header = request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(header))
                return false;

            if (!AuthenticationHeaderValue.TryParse(header, out var auth) || auth is null)
                return false;

            if (!string.Equals(auth.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(auth.Parameter))
                return false;

            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter));
            }
            catch (FormatException)
            {
                return false;
            }

            var separator = decoded.IndexOf(':');
            if (separator < 0)
                return false;

            var username = decoded[..separator];
            var password = decoded[(separator + 1)..];

            return FixedTimeEquals(username, expectedUsername)
                   && FixedTimeEquals(password, expectedPassword);
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            var aBytes = Encoding.UTF8.GetBytes(a);
            var bBytes = Encoding.UTF8.GetBytes(b);
            return aBytes.Length == bBytes.Length
                   && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
    }
}
