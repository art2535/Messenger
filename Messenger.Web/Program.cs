using Messenger.Web.Extensions;
using Messenger.Web.Helpers;
using System.Net.Http.Headers;
using System.Text;

namespace Messenger.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (Console.IsOutputRedirected)
            {
                Console.OutputEncoding = Encoding.UTF8;
            }

            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddRazorPages();
            builder.Services.AddEtaWebAuthentication(builder.Configuration);
            builder.Services.AddLogging();
            builder.Services.AddAuthorization();
            builder.Services.AddSignalRService(builder.Configuration);
            builder.Services.AddScoped<ApiHelper>();
            builder.Services.AddApiVersioning()
                .AddApiExplorer(options =>
                {
                    options.GroupNameFormat = "'v'VVV";
                    options.SubstituteApiVersionInUrl = true;
                });
            builder.Services.AddHttpClient("Api", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });
            builder.Services.AddDistributedMemoryCache();

            builder.Services.AddBackgroundTokenRefresh(builder.Configuration);

            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromHours(1);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
            });

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStaticFiles();
            app.Map("/uploads/{**path}", async (string path, HttpContext ctx, IHttpClientFactory httpClientFactory) =>
            {
                var apiBase = builder.Configuration["URL:API:HTTPS"]?.TrimEnd('/');
                if (string.IsNullOrEmpty(apiBase))
                    return Results.Problem("URL:API:HTTPS не настроен");

                var targetUrl = $"{apiBase}/uploads/{path}{ctx.Request.QueryString}";
                var client = httpClientFactory.CreateClient("Api");
                using var response = await client.GetAsync(targetUrl, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
                if (!response.IsSuccessStatusCode)
                    return Results.StatusCode((int)response.StatusCode);

                var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                var stream = await response.Content.ReadAsStreamAsync(ctx.RequestAborted);
                return Results.Stream(stream, contentType, enableRangeProcessing: true);
            });

            app.UseSession();
            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapRazorPages();
            app.MapStaticAssets();
            app.MapControllers();

            app.Run();
        }
    }
}