using Microsoft.AspNetCore.Http;

namespace Messenger.API.Extensions;

/// <summary>
/// Добавляет базовые HTTP security headers ко всем ответам API.
/// </summary>
public static class SecurityHeadersExtension
{
    public static WebApplication UseMessengerSecurityHeaders(
        this WebApplication app,
        bool includeContentSecurityPolicy = false)
    {
        app.Use(async (context, next) =>
        {
            ApplySecurityHeaders(context.Response, includeContentSecurityPolicy);

            if (context.Request.Path.StartsWithSegments("/api") ||
                context.Request.Path.StartsWithSegments("/oauth"))
            {
                context.Response.OnStarting(() =>
                {
                    if (!context.Response.Headers.ContainsKey("Cache-Control"))
                        context.Response.Headers.CacheControl = "no-store";

                    return Task.CompletedTask;
                });
            }

            await next();
        });

        return app;
    }

    public static void ApplySecurityHeaders(
        HttpResponse response,
        bool includeContentSecurityPolicy = false)
    {
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.XFrameOptions = "DENY";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

        if (includeContentSecurityPolicy)
        {
            response.Headers.ContentSecurityPolicy =
                "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        }
    }
}
