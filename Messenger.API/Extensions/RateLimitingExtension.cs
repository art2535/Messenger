using System.Security.Claims;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Messenger.API.Extensions;

public static class RateLimitingExtension
{
    private const int ApiPermitLimit = 120;
    private static readonly TimeSpan ApiWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Applies a distributed per-user fixed-window limit to MVC controller endpoints.
    /// Must run after routing and authentication.
    /// </summary>
    public static WebApplication UseMessengerRateLimiting(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is null)
            {
                await next();
                return;
            }

            var userId = context.User.FindFirst("sub")?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";

            var limiter = context.RequestServices.GetRequiredService<IRateLimitService>();
            var acquired = await limiter.TryAcquireAsync(
                "http:api:" + userId,
                ApiPermitLimit,
                ApiWindow,
                context.RequestAborted);

            if (!acquired)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(
                    """{"isSuccess":false,"error":"Слишком много запросов. Подождите немного и попробуйте снова."}""",
                    context.RequestAborted);
                return;
            }

            await next(context);
        });

        return app;
    }
}
