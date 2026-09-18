using System.Security.Claims;
using System.Threading.RateLimiting;

namespace Messenger.API.Extensions
{
    public static class RateLimitingExtension
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddMessengerRateLimiting()
            {
                services.AddRateLimiter(options =>
                {
                    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                    options.AddPolicy("api", httpContext =>
                    {
                        var userId = GetUserId(httpContext);
                        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 20,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                        });
                    });

                    options.AddPolicy("send-message", httpContext =>
                    {
                        var userId = GetUserId(httpContext);
                        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 5,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                        });
                    });

                    options.AddPolicy("typing", httpContext =>
                    {
                        var userId = GetUserId(httpContext);
                        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 60,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        });
                    });

                    options.OnRejected = async (context, ct) =>
                    {
                        context.HttpContext.Response.ContentType = "application/json";
                        await context.HttpContext.Response.WriteAsync(
                            """{"isSuccess":false,"error":"Too many requests. Please slow down."}""", ct);
                    };
                });

                return services;
            }
        }

        private static string GetUserId(HttpContext httpContext)
        {
            var sub = httpContext.User.FindFirst("sub")?.Value
                   ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(sub))
                return sub;

            return httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        }
    }
}