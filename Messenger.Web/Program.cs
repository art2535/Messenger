using Messenger.Web.Extensions;
using Messenger.Web.Helpers;
using Microsoft.AspNetCore.ResponseCompression;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System.IO.Compression;
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

            Console.WriteLine("=============================================================");
            Console.WriteLine($"[WEB DEBUG] Среда: {builder.Environment.EnvironmentName}");
            Console.WriteLine($"[WEB DEBUG] ASPNETCORE_URLS: '{builder.Configuration["ASPNETCORE_URLS"]}'");
            Console.WriteLine($"[WEB DEBUG] ASPNETCORE_HTTPS_PORTS: '{builder.Configuration["ASPNETCORE_HTTPS_PORTS"]}'");
            Console.WriteLine("=============================================================");

            builder.ConfigureDevelopmentCertificate();

            builder.Services.AddRazorPages();
            builder.Services.AddEtaWebAuthentication(builder.Configuration);
            builder.Services.AddLogging();

            builder.Services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService(
                    serviceName: "Messenger.Web",
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"))
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddPrometheusExporter();
                });

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

            builder.Services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
                {
                    "application/javascript",
                    "application/json",
                    "text/css",
                    "text/html",
                    "text/plain",
                    "image/svg+xml",
                    "application/font-woff",
                    "application/font-woff2",
                    "font/woff",
                    "font/woff2"
                });
            });

            builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
            builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseResponseCompression();

            var staticFileOptions = new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    var path = ctx.Context.Request.Path.Value?.ToLowerInvariant() ?? "";
                    if (path.StartsWith("/lib/") ||
                        path.StartsWith("/css/") ||
                        path.StartsWith("/js/") ||
                        path.StartsWith("/images/") ||
                        path.EndsWith(".woff") ||
                        path.EndsWith(".woff2") ||
                        path.EndsWith(".ttf") ||
                        path.EndsWith(".eot"))
                    {
                        ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                        ctx.Context.Response.Headers.Append("Expires", DateTime.UtcNow.AddYears(1).ToString("R"));
                    }
                    else
                    {
                        ctx.Context.Response.Headers.CacheControl = "public, max-age=86400";
                    }
                }
            };

            app.UseStaticFiles(staticFileOptions);

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

            app.MapPrometheusScrapingEndpoint("/metrics");

            app.Run();
        }
    }
}