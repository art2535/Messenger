using Microsoft.Extensions.FileProviders;

namespace Messenger.API.Extensions
{
    public static class StaticFilesExtension
    {
        extension(WebApplication app)
        {
            /// <summary>
            /// Публикует только публичные статические ресурсы аватаров.
            /// Файлы сообщений из /uploads выдаются через авторизованный endpoint.
            /// </summary>
            public IApplicationBuilder UseStaticAssets()
            {
                var avatarsPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "avatars");

                if (!Directory.Exists(avatarsPath))
                {
                    Directory.CreateDirectory(avatarsPath);
                }

                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(avatarsPath),
                    RequestPath = "/avatars",
                    OnPrepareResponse = ctx =>
                    {
                        ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                    }
                });

                return app;
            }
        }
    }
}