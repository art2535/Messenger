namespace Messenger.Web.Extensions;

public static class SecurityHeadersExtension
{
    public static WebApplication UseMessengerWebSecurityHeaders(
        this WebApplication app,
        bool includeContentSecurityPolicy = false)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.XFrameOptions = "DENY";
            context.Response.Headers.ReferrerPolicy = "no-referrer";
            context.Response.Headers["Permissions-Policy"] =
                "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

            if (includeContentSecurityPolicy)
            {
                context.Response.Headers.ContentSecurityPolicy =
                    "default-src 'self'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'; form-action 'self'";
            }

            await next();
        });

        return app;
    }
}
