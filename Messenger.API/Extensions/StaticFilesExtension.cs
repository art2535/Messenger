using Microsoft.Extensions.FileProviders;

namespace Messenger.API.Extensions
{
    public static class StaticFilesExtension
    {
        extension(WebApplication app)
        {
            public IApplicationBuilder UseUploads()
            {
                var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");

                if (!Directory.Exists(uploadsPath))
                {
                    Directory.CreateDirectory(uploadsPath);
                }

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(uploadsPath),
                    RequestPath = "/uploads",
                    OnPrepareResponse = ctx =>
                    {
                        var path = ctx.Context.Request.Path.Value?.ToLowerInvariant() ?? "";
                        var query = ctx.Context.Request.QueryString.Value ?? "";

                        // SVG может содержать активное содержимое и не должен открываться inline с origin приложения.
                        var inlineImageExtensions = new HashSet<string>
                        {
                            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".avif"
                        };

                        var extension = Path.GetExtension(path);
                        var forceDownload = !inlineImageExtensions.Contains(extension)
                            || query.Contains("download", StringComparison.OrdinalIgnoreCase);

                        ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";

                        if (forceDownload)
                        {
                            var fileName = Path.GetFileName(path);
                            ctx.Context.Response.Headers.ContentDisposition =
                                $"attachment; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
                        }
                    }
                });

                var avatarsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "avatars");

                if (!Directory.Exists(avatarsPath))
                {
                    Directory.CreateDirectory(avatarsPath);
                }

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(avatarsPath),
                    RequestPath = "/avatars"
                });

                return app;
            }
        }
    }
}
